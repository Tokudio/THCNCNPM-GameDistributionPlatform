using System.Data;
using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.ExceptionServices;
using GameDistributionPlatform.Application.DTOs;
using GameDistributionPlatform.Application.Exceptions;
using GameDistributionPlatform.Application.Interfaces;
using GameDistributionPlatform.Domain.Entities;
using GameDistributionPlatform.Domain.Enums;
using GameDistributionPlatform.Infrastructure.Services;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace GameDistributionPlatform.WebApi.Tests.SqlServer;

[Collection(SqlServerCollection.Name)]
[Trait("Database", "SqlServer")]
public sealed class SqlServerConcurrencyTests(SqlServerFixture fixture, ITestOutputHelper output)
{
    private static readonly CheckoutAvailability Available = new(true);
    private const string Users = "/api/v1/admin/users";
    private const string AdminOrders = "/api/v1/admin/orders";

    [SqlServerFact]
    public async Task ConcurrentAdminRequestsCanDisableAndReactivateMemberIdempotently()
    {
        await using var data = fixture.BeginScenario();
        using var first = data.Client(await data.UserAsync("Admin"));
        using var second = data.Client(await data.UserAsync("Admin"));
        var member = await data.UserAsync();
        using var memberClient = data.Client(member);
        var responses = await LiveApi.RaceAsync(() => SetStatusAsync(first, member, false),
            () => SetStatusAsync(second, member, false));
        foreach (var response in responses)
        {
            using (response) Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }
        await using var check = fixture.NewContext();
        Assert.False(await check.Users.Where(user => user.Id == member).Select(user => user.IsActive).SingleAsync());
        using var denied = await memberClient.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        using var activated = await SetStatusAsync(first, member, true);
        Assert.Equal(HttpStatusCode.NoContent, activated.StatusCode);
        using var restored = await memberClient.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        Assert.True(await check.Users.Where(user => user.Id == member).Select(user => user.IsActive).SingleAsync());
        Assert.Equal(1, await check.UserRoles.CountAsync(entry => entry.UserId == member));
    }

    [SqlServerFact]
    public async Task ConcurrentAdminDeactivationLeavesOneActiveAdminAndRejectsSelfDeactivation()
    {
        await using var data = fixture.BeginScenario();
        var firstId = await data.UserAsync("Admin");
        var secondId = await data.UserAsync("Admin");
        using var first = data.Client(firstId);
        using var second = data.Client(secondId);
        var responses = await LiveApi.RaceAsync(() => SetStatusAsync(first, secondId, false),
            () => SetStatusAsync(second, firstId, false));
        using var firstResponse = responses[0];
        using var secondResponse = responses[1];
        Assert.Equal(new[] { HttpStatusCode.NoContent, HttpStatusCode.Unauthorized }, responses.Select(response => response.StatusCode).Order());
        var survivor = firstResponse.IsSuccessStatusCode ? first : second;
        var survivorId = firstResponse.IsSuccessStatusCode ? firstId : secondId;
        var disabled = firstResponse.IsSuccessStatusCode ? second : first;
        using var denied = await disabled.GetAsync(Users);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        using var protect = await SetStatusAsync(survivor, survivorId, false);
        Assert.Equal(HttpStatusCode.Conflict, protect.StatusCode);
        await using var db = fixture.NewContext();
        var activeAdmins = await (from user in db.Users
            join link in db.UserRoles on user.Id equals link.UserId
            join role in db.Roles on link.RoleId equals role.Id
            where user.IsActive && role.NormalizedName == "ADMIN"
            select user.Id).ToArrayAsync();
        Assert.Equal(survivorId, Assert.Single(activeAdmins));
    }

    [SqlServerFact]
    public async Task AdminOrderDetailWaitsForCheckoutCommitAndReturnsOneConsistentSnapshot()
    {
        await using var data = fixture.BeginScenario();
        var buyerId = await data.UserAsync();
        using var buyer = data.Client(buyerId);
        using var admin = data.Client(await data.UserAsync("Admin"));
        var gameId = await data.GameAsync();
        var (_, order) = await LiveApi.CreateAsync(buyer, gameId);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(18));
        var commitReached = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var continueCommit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var beforeCommit = new CommitHook(before: async (transaction, token) =>
        {
            Assert.Equal(IsolationLevel.Serializable, transaction.IsolationLevel);
            commitReached.TrySetResult(((SqlConnection)transaction.Connection!).ServerProcessId);
            await continueCommit.Task.WaitAsync(token);
        });
        var observe = new SqlProbe("FROM [Orders]");
        await using var writeDb = fixture.NewContext(beforeCommit);
        await using var readDb = fixture.NewContext(observe);
        var checkout = new OrderService(writeDb, Available)
            .CompleteMockPaymentAsync(buyerId, order.Id, MockPaymentOutcome.Success, deadline.Token);
        Task<AdminOrderDetailResponse>? detail = null;
        try
        {
            var writerSpid = await commitReached.Task.WaitAsync(deadline.Token);
            detail = new AdminReportingService(readDb).GetOrderAsync(order.Id, deadline.Token);
            var read = await observe.Started.Task.WaitAsync(deadline.Token);
            Assert.Equal(IsolationLevel.Serializable, read.Isolation);
            await AssertBlockedAsync(read.Session, writerSpid, deadline.Token);
        }
        catch
        {
            deadline.Cancel();
            await ((Task)checkout).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            if (detail is not null) await ((Task)detail).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            throw;
        }
        finally
        {
            continueCommit.TrySetResult();
        }

        Assert.Equal(PaymentStatus.Succeeded, (await checkout).PaymentStatus);
        var report = await detail;
        Assert.Equal(OrderStatus.Paid, report.Order.Status);
        Assert.Equal(PaymentStatus.Succeeded, report.Order.PaymentStatus);
        Assert.NotNull(report.PaidAt);
        Assert.Equal(gameId, Assert.Single(report.Items).GameId);
        Assert.Equal(order.TotalAmount, Assert.Single(report.Payments).Amount);
        Assert.Equal(PaymentStatus.Succeeded, Assert.Single(report.Payments).Status);
        var summary = await admin.GetFromJsonAsync<PageResponse<AdminOrderSummaryResponse>>($"{AdminOrders}?search={order.Id}");
        Assert.Equal(OrderStatus.Paid, Assert.Single(summary!.Items).Status);
        var dashboard = await admin.GetFromJsonAsync<DashboardSummaryResponse>("/api/v1/admin/dashboard/summary");
        Assert.Equal(order.TotalAmount, Assert.Single(dashboard!.Revenue).Amount);
        await using var check = fixture.NewContext();
        Assert.Equal(OrderStatus.Paid, await check.Orders.Where(row => row.Id == order.Id).Select(row => row.Status).SingleAsync());
        Assert.Equal(PaymentStatus.Succeeded, await check.Payments.Where(row => row.OrderId == order.Id).Select(row => row.Status).SingleAsync());
        Assert.Equal(gameId, await check.UserLibraries.Where(row => row.UserId == buyerId).Select(row => row.GameId).SingleAsync());
    }

    [SqlServerFact]
    public async Task RealDeadlockReturns1205AndCheckoutConflictThenSucceedsOnRetry()
    {
        await using var data = fixture.BeginScenario();
        var user = await data.UserAsync();
        var game = await data.GameAsync();
        using var client = data.Client(user);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(18));
        var probe = new SqlProbe("WITH (UPDLOCK, HOLDLOCK)");
        await using var db = fixture.NewContext(probe);
        await using var blocker = await fixture.OpenConnectionAsync();
        using (var priority = SqlServerFixture.Command(blocker, "SET DEADLOCK_PRIORITY HIGH;"))
            await priority.ExecuteNonQueryAsync(deadline.Token);
        await using var transaction = (SqlTransaction)await blocker.BeginTransactionAsync(deadline.Token);
        await UpdateAsync(blocker, transaction, "UPDATE dbo.Games SET Price = Price + 1 WHERE Id = @id;", game, deadline.Token);

        await db.Database.OpenConnectionAsync(deadline.Token);
        var connectionId = ((SqlConnection)db.Database.GetDbConnection()).ClientConnectionId;
        var deadlock = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        void ObserveError(object? sender, FirstChanceExceptionEventArgs args)
        {
            // Ghi nhận mã SQL ngay cả khi provider ném lỗi trong lúc đọc kết quả và service chuyển thành ConflictException.
            if (args.Exception is SqlException { Number: 1205 } sql && sql.ClientConnectionId == connectionId)
                deadlock.TrySetResult(sql.Number);
        }

        AppDomain.CurrentDomain.FirstChanceException += ObserveError;
        var checkout = new OrderService(db, Available).CreateAsync(user, game, deadline.Token);
        Task? finish = null;
        try
        {
            var read = await probe.Started.Task.WaitAsync(deadline.Token);
            Assert.Equal(IsolationLevel.Serializable, read.Isolation);
            Assert.Contains("[AspNetUsers] WITH (UPDLOCK, HOLDLOCK)", read.Sql);
            await AssertBlockedAsync(read.Session, blocker.ServerProcessId, deadline.Token);

            // Hai phiên giữ lock trên hai dòng fixture theo thứ tự đảo ngược.
            finish = UpdateAsync(blocker, transaction, "UPDATE dbo.AspNetUsers SET DisplayName = N'Deadlock fixture' WHERE Id = @id;",
                user, deadline.Token);
            await Assert.ThrowsAsync<ConflictException>(() => checkout);
            await finish;
            var number = await deadlock.Task.WaitAsync(TimeSpan.FromSeconds(2), deadline.Token);
            Assert.Equal(1205, number);
            output.WriteLine("SQL Server deadlock victim error: {0}. Checkout mapped it to ConflictException.", number);
        }
        finally
        {
            AppDomain.CurrentDomain.FirstChanceException -= ObserveError;
            if (!checkout.IsCompleted) deadline.Cancel();
            await ((Task)checkout).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            if (finish is not null) await finish.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }

        await transaction.RollbackAsync();
        await using var check = fixture.NewContext();
        Assert.Empty(await check.Orders.Where(row => row.UserId == user).ToListAsync());
        Assert.Equal(19.99m, await check.Games.Where(row => row.Id == game).Select(row => row.Price).SingleAsync());
        Assert.Equal("SQL Live Fixture", await check.Users.Where(row => row.Id == user).Select(row => row.DisplayName).SingleAsync());
        var retry = await LiveApi.CreateAsync(client, game);
        Assert.Equal(HttpStatusCode.Created, retry.Status);
        Assert.Equal(retry.Order.Id, await check.Orders.Where(row => row.UserId == user).Select(row => row.Id).SingleAsync());
    }

    [SqlServerFact]
    public async Task SqlServerLockTimeout1222DoesNotWriteAnOrderAndFreshRequestCanRetry()
    {
        await using var data = fixture.BeginScenario();
        var user = await data.UserAsync();
        var game = await data.GameAsync();
        using var client = data.Client(user);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var blocker = await fixture.OpenConnectionAsync();
        await using var transaction = (SqlTransaction)await blocker.BeginTransactionAsync(deadline.Token);
        await UpdateAsync(blocker, transaction,
            "UPDATE dbo.AspNetUsers SET DisplayName = N'Lock timeout fixture' WHERE Id = @id;", user, deadline.Token);

        await using (var db = fixture.NewContext())
        {
            await db.Database.OpenConnectionAsync(deadline.Token);
            await db.Database.ExecuteSqlRawAsync("SET LOCK_TIMEOUT 500;", deadline.Token);
            var error = await Assert.ThrowsAsync<SqlException>(() => new OrderService(db, Available).CreateAsync(user, game, deadline.Token));
            Assert.Equal(1222, error.Number);
            output.WriteLine("SQL Server lock timeout error: {0}.", error.Number);
        }
        await transaction.RollbackAsync(deadline.Token);
        await using var check = fixture.NewContext();
        Assert.Empty(await check.Orders.Where(row => row.UserId == user).ToListAsync());
        var retry = await LiveApi.CreateAsync(client, game);
        Assert.Equal(HttpStatusCode.Created, retry.Status);
        Assert.Equal(retry.Order.Id, await check.Orders.Where(row => row.UserId == user).Select(row => row.Id).SingleAsync());
    }

    [SqlServerFact]
    public async Task SqlServer2601ProtectsPaymentIdempotencyAndAttemptNumber()
    {
        await using var data = fixture.BeginScenario();
        var user = await data.UserAsync();
        var game = await data.GameAsync();
        using var client = data.Client(user);
        var (_, order) = await LiveApi.CreateAsync(client, game);
        using var response = await LiveApi.PayAsync(client, order.Id);
        Assert.Equal(PaymentStatus.Succeeded, (await LiveApi.ReadPaymentAsync(response)).PaymentStatus);
        await using var check = fixture.NewContext();
        var saved = await check.Payments.SingleAsync(row => row.OrderId == order.Id);

        foreach (var attempt in new[]
        {
            (Number: 2, Key: saved.IdempotencyKey, Index: "IX_Payments_OrderId_IdempotencyKey"),
            (Number: 1, Key: $"sql-live:{Guid.NewGuid():N}", Index: "IX_Payments_OrderId_AttemptNumber")
        })
        {
            await using var db = fixture.NewContext();
            db.Payments.Add(new Payment
            {
                OrderId = order.Id, IdempotencyKey = attempt.Key, AttemptNumber = attempt.Number, Amount = saved.Amount
            });
            var error = await AssertSqlErrorAsync(db.SaveChangesAsync, 2601, attempt.Index);
            output.WriteLine("SQL Server {0}: {1}.", error, attempt.Index);
        }
        Assert.Equal(saved.Id, await check.Payments.Where(row => row.OrderId == order.Id).Select(row => row.Id).SingleAsync());
        Assert.Equal(1, await check.UserLibraries.CountAsync(row => row.UserId == user && row.GameId == game));
    }

    [SqlServerFact]
    public async Task SqlServer2601ProtectsUserGameOwnershipAnd2627ProtectsPrimaryKey()
    {
        await using var data = fixture.BeginScenario();
        var user = await data.UserAsync();
        var game = await data.GameAsync(price: 0);
        using var client = data.Client(user);
        var (_, order) = await LiveApi.CreateAsync(client, game);
        Assert.Equal(OrderStatus.Paid, order.Status);
        await using (var db = fixture.NewContext())
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            // Dùng OrderItem khác để kiểm tra đúng unique index UserId/GameId, không chạm index OrderItemId.
            var otherOrder = new Order { OrderCode = $"SQL-{Guid.NewGuid():N}", UserId = user };
            var otherItem = new OrderItem { Order = otherOrder, GameId = game, GameTitle = "SQL Live Fixture" };
            db.OrderItems.Add(otherItem);
            await db.SaveChangesAsync();
            db.UserLibraries.Add(new UserLibrary { UserId = user, GameId = game, OrderItemId = otherItem.Id });
            var error = await AssertSqlErrorAsync(db.SaveChangesAsync, 2601, "IX_UserLibraries_UserId_GameId");
            output.WriteLine("SQL Server {0}: IX_UserLibraries_UserId_GameId.", error);
            await transaction.RollbackAsync();
        }
        await using (var db = fixture.NewContext())
        {
            db.Games.Add(new Game { Id = game, Slug = $"sql-live-{Guid.NewGuid():N}", Title = "Duplicate key fixture" });
            var error = await AssertSqlErrorAsync(db.SaveChangesAsync, 2627, "PK_Games");
            output.WriteLine("SQL Server {0}: PK_Games.", error);
        }
        await using var check = fixture.NewContext();
        Assert.Equal(order.Id, await check.Orders.Where(row => row.UserId == user).Select(row => row.Id).SingleAsync());
        Assert.Equal(1, await check.UserLibraries.CountAsync(row => row.UserId == user && row.GameId == game));
        Assert.False(await check.Payments.AnyAsync(row => row.OrderId == order.Id));
        Assert.Equal(1, await check.Games.CountAsync(row => row.Id == game));
    }

    private async Task AssertBlockedAsync(int waiter, int blocker, CancellationToken cancellationToken)
    {
        Assert.NotEqual(waiter, blocker);
        await using var observer = await fixture.OpenConnectionAsync();
        using var command = SqlServerFixture.Command(observer, """
            SELECT s.transaction_isolation_level
            FROM sys.dm_exec_requests r JOIN sys.dm_exec_sessions s ON s.session_id = r.session_id
            WHERE r.session_id = @waiter AND r.blocking_session_id = @blocker AND r.wait_type LIKE N'LCK%';
            """);
        command.Parameters.Add("@waiter", SqlDbType.Int).Value = waiter;
        command.Parameters.Add("@blocker", SqlDbType.Int).Value = blocker;
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        wait.CancelAfter(TimeSpan.FromSeconds(5));
        var result = await command.ExecuteScalarAsync(wait.Token);
        while (result is null)
        {
            await Task.Delay(25, wait.Token);
            result = await command.ExecuteScalarAsync(wait.Token);
        }
        var isolation = Assert.IsType<short>(result);
        Assert.Equal(4, isolation);
        output.WriteLine("SQL Server DMV observed a lock wait between separate sessions at isolation level {0} (SERIALIZABLE).", isolation);
    }

    private static async Task UpdateAsync(SqlConnection connection, SqlTransaction transaction, string sql, Guid id,
        CancellationToken cancellationToken)
    {
        using var command = SqlServerFixture.Command(connection, sql, transaction);
        command.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = id;
        Assert.Equal(1, await command.ExecuteNonQueryAsync(cancellationToken));
    }

    private static async Task<int> AssertSqlErrorAsync(Func<CancellationToken, Task<int>> save, int number, string index)
    {
        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => save(default));
        var sql = Assert.IsType<SqlException>(failure.GetBaseException());
        Assert.Equal(number, sql.Number);
        Assert.Contains(index, sql.Message);
        return sql.Number;
    }

    private static Task<HttpResponseMessage> SetStatusAsync(HttpClient client, Guid id, bool active) =>
        client.PatchAsJsonAsync($"{Users}/{id}/status", new { isActive = active });

    private sealed class SqlProbe(string marker) : DbCommandInterceptor
    {
        internal TaskCompletionSource<(int Session, IsolationLevel? Isolation, string Sql)> Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains(marker, StringComparison.Ordinal))
                Started.TrySetResult((((SqlConnection)command.Connection!).ServerProcessId, command.Transaction?.IsolationLevel, command.CommandText));
            return ValueTask.FromResult(result);
        }

    }
}
