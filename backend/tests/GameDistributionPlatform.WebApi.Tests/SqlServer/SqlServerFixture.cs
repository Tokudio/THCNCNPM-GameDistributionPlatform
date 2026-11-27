using System.Data;
using System.Data.Common;
using System.Net.Http.Headers;
using GameDistributionPlatform.Domain.Entities;
using GameDistributionPlatform.Domain.Enums;
using GameDistributionPlatform.Infrastructure.Data;
using GameDistributionPlatform.Infrastructure.Data.Seeding;
using GameDistributionPlatform.Infrastructure.Identity;
using GameDistributionPlatform.WebApi.Tests.Fixtures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace GameDistributionPlatform.WebApi.Tests.SqlServer;

internal sealed class SqlServerFactAttribute : FactAttribute
{
    internal const string EnableVariable = "GDP_SQLSERVER_LIVE_TESTS";
    internal static bool Enabled => Environment.GetEnvironmentVariable(EnableVariable) == "1";

    public SqlServerFactAttribute()
    {
        if (!Enabled) Skip = $"Bật {EnableVariable}=1 để chạy trên database SQL Server Testing.";
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "SQL Server Live";
}

public sealed class SqlServerFixture : IAsyncLifetime
{
    private const string Server = "LAPTOP-UI1V2NBG";
    private const string Database = "GameDistributionPlatform_Test";
    private const int CommandTimeout = 12;
    private static readonly string ConnectionString = new SqlConnectionStringBuilder
    {
        DataSource = "lpc:" + Server,
        InitialCatalog = Database,
        IntegratedSecurity = true,
        Encrypt = SqlConnectionEncryptOption.Mandatory,
        TrustServerCertificate = true,
        ApplicationName = "GameDistributionPlatform.SqlServerTests",
        ConnectTimeout = 5,
        ConnectRetryCount = 0,
        Pooling = false
    }.ConnectionString;

    private static readonly string[] BusinessTables =
    [
        "AspNetRoleClaims", "AspNetRoles", "AspNetUserClaims", "AspNetUserLogins", "AspNetUserRoles",
        "AspNetUsers", "AspNetUserTokens", "Categories", "GameCategories", "GameMedia", "Games",
        "OrderItems", "Orders", "Payments", "UserLibraries"
    ];

    private readonly ConnectionGuard _guard = new();
    private readonly IdentityRole<Guid>[] _roles = [CreateRole("Admin"), CreateRole("User")];
    private readonly IReadOnlyList<Category> _categories = SeedConstants.DefaultCategories;
    private SqlConnection? _suiteLock;
    private WebApplicationFactory<Program>? _factory;
    private bool _ownsSeedData;
    private bool _ready;

    public async Task InitializeAsync()
    {
        if (!SqlServerFactAttribute.Enabled) return;

        _suiteLock = await OpenConnectionAsync();
        using var lease = Command(_suiteLock, """
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock
                @Resource = N'GameDistributionPlatform.SqlServerLiveTests',
                @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = 0;
            SELECT @result;
            """);
        if ((int)(await lease.ExecuteScalarAsync())! < 0)
            throw new InvalidOperationException("Một SQL Server test suite khác đang sử dụng database Testing.");

        await VerifySchemaAndEmptyTablesAsync(_suiteLock);
        await using var db = NewContext();
        Assert.True(db.Database.IsSqlServer());
        Assert.False(db.Database.HasPendingModelChanges());

        // Giữ ID các bản ghi mặc định để startup seeder thấy chúng đã tồn tại và fixture dọn được theo khóa.
        _ownsSeedData = true;
        db.AddRange(_roles);
        db.AddRange(_categories);
        await db.SaveChangesAsync();

        _factory = new SqlServerHost(this);
        await using var scope = _factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        Assert.Equal("Testing", services.GetRequiredService<IHostEnvironment>().EnvironmentName);
        var config = services.GetRequiredService<IConfiguration>();
        Assert.Equal(ConnectionString, config.GetConnectionString("DefaultConnection"));
        Assert.Equal(string.Empty, config["BootstrapAdmin:Email"]);
        var hostDb = services.GetRequiredService<AppDbContext>();
        Assert.True(hostDb.Database.IsSqlServer());
        Assert.Equal(ConnectionString, hostDb.Database.GetConnectionString());
        Assert.Equal(_roles.Select(role => role.Id).Order(), (await hostDb.Roles.Select(role => role.Id).ToArrayAsync()).Order());
        Assert.Equal(_categories.Select(category => category.Id).Order(), (await hostDb.Categories.Select(category => category.Id).ToArrayAsync()).Order());
        Assert.Empty(await hostDb.Users.Select(user => user.Id).ToListAsync());
        _ready = true;
    }

    public Scenario BeginScenario()
    {
        Assert.True(_ready);
        return new Scenario(this);
    }

    public AppDbContext NewContext(params IInterceptor[] interceptors)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>();
        ConfigureContext(options);
        return new AppDbContext(options.AddInterceptors(interceptors).Options);
    }

    private void ConfigureContext(DbContextOptionsBuilder options) =>
        options.UseSqlServer(ConnectionString, sql => sql.CommandTimeout(CommandTimeout)).AddInterceptors(_guard);

    internal async Task<SqlConnection> OpenConnectionAsync()
    {
        if (!SqlServerFactAttribute.Enabled)
            throw new InvalidOperationException("SQL Server live tests chưa được bật.");

        var connection = new SqlConnection(ConnectionString);
        try
        {
            await connection.OpenAsync();
            await VerifyConnectionAsync(connection, default);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    internal static SqlCommand Command(SqlConnection connection, string sql, SqlTransaction? transaction = null) =>
        new(sql, connection, transaction) { CommandTimeout = CommandTimeout };

    private static async Task VerifyConnectionAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        using var command = Command(connection, """
            SELECT CONVERT(nvarchar(128), SERVERPROPERTY('ServerName')), DB_NAME(),
                   CONVERT(nvarchar(40), CONNECTIONPROPERTY('net_transport')),
                   CONVERT(nvarchar(40), CONNECTIONPROPERTY('auth_scheme')),
                   SCHEMA_NAME(), d.state_desc, d.is_read_only
            FROM sys.databases d WHERE d.database_id = DB_ID();
            """);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) ||
            !string.Equals(reader.GetString(0), Server, StringComparison.OrdinalIgnoreCase) ||
            reader.GetString(1) != Database || reader.GetString(2) != "Shared memory" ||
            reader.GetString(3) is not ("NTLM" or "KERBEROS") || reader.GetString(4) != "dbo" ||
            reader.GetString(5) != "ONLINE" || reader.GetBoolean(6))
            throw new InvalidOperationException("Phiên SQL Server không khớp database Testing đã xác minh.");
    }

    private static async Task VerifySchemaAndEmptyTablesAsync(SqlConnection connection)
    {
        using var command = Command(connection, """
            SELECT s.name, t.name
            FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id
            WHERE t.is_ms_shipped = 0;
            SELECT MigrationId, ProductVersion FROM dbo.__EFMigrationsHistory ORDER BY MigrationId;
            SELECT COUNT(*) FROM sys.foreign_keys WHERE is_disabled = 0 AND is_not_trusted = 0;
            SELECT COUNT(*) FROM sys.indexes i JOIN sys.tables t ON i.object_id = t.object_id
            WHERE t.is_ms_shipped = 0 AND i.is_unique = 1 AND i.is_primary_key = 0 AND i.is_disabled = 0;
            """);
        await using (var reader = await command.ExecuteReaderAsync())
        {
            var actual = new List<string>();
            while (await reader.ReadAsync()) actual.Add($"{reader.GetString(0)}.{reader.GetString(1)}");
            Assert.Equal(BusinessTables.Append("__EFMigrationsHistory").Select(table => "dbo." + table).Order(), actual.Order());
            Assert.True(await reader.NextResultAsync());
            foreach (var migration in new[] { "20261108032935_InitialCreate", "20261114091110_AddUniqueIndexToNormalizedEmail" })
            {
                Assert.True(await reader.ReadAsync());
                Assert.Equal(migration, reader.GetString(0));
                Assert.Equal("10.0.15", reader.GetString(1));
            }
            Assert.False(await reader.ReadAsync());
            foreach (var count in new[] { 16, 11 })
            {
                Assert.True(await reader.NextResultAsync());
                Assert.True(await reader.ReadAsync());
                Assert.Equal(count, reader.GetInt32(0));
            }
        }

        // Danh sách tên bảng cố định nhằm kiểm tra chính xác số dòng mà không đọc nội dung.
        command.CommandText = string.Join(" UNION ALL ", BusinessTables.Select(table =>
            $"SELECT N'{table}', COUNT_BIG(*) FROM [dbo].[{table}]"));
        await using var counts = await command.ExecuteReaderAsync();
        while (await counts.ReadAsync())
            Assert.True(counts.GetInt64(1) == 0, $"Bảng {counts.GetString(0)} phải trống để chạy SQL Server test suite.");
    }

    public async Task DisposeAsync()
    {
        try
        {
            if (_factory is not null) await _factory.DisposeAsync();
            if (_ownsSeedData)
            {
                await using var db = NewContext();
                var categoryIds = _categories.Select(category => category.Id).ToArray();
                var roleIds = _roles.Select(role => role.Id).ToArray();
                await using var transaction = await db.Database.BeginTransactionAsync();
                await db.Categories.Where(category => categoryIds.Contains(category.Id)).ExecuteDeleteAsync();
                await db.Roles.Where(role => roleIds.Contains(role.Id)).ExecuteDeleteAsync();
                await transaction.CommitAsync();
                await VerifySchemaAndEmptyTablesAsync(_suiteLock!);
            }
        }
        finally
        {
            if (_suiteLock is not null) await _suiteLock.DisposeAsync();
        }
    }

    private static IdentityRole<Guid> CreateRole(string name) =>
        new() { Id = Guid.NewGuid(), Name = name, NormalizedName = name.ToUpperInvariant(), ConcurrencyStamp = Guid.NewGuid().ToString("N") };

    private sealed class ConnectionGuard : DbConnectionInterceptor
    {
        public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
        {
            CheckSettings(connection);
            return result;
        }

        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default)
        {
            CheckSettings(connection);
            return ValueTask.FromResult(result);
        }

        public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) =>
            VerifyConnectionAsync((SqlConnection)connection, default).GetAwaiter().GetResult();

        public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData,
            CancellationToken cancellationToken = default) => VerifyConnectionAsync((SqlConnection)connection, cancellationToken);

        private static void CheckSettings(DbConnection connection)
        {
            if (!SqlServerFactAttribute.Enabled || connection is not SqlConnection || connection.ConnectionString != ConnectionString)
                throw new InvalidOperationException("AppDbContext phải dùng cấu hình SQL Server Testing được chỉ định.");
        }
    }

    private sealed class SqlServerHost(SqlServerFixture fixture) : WebApplicationFactory<Program>
    {
        private static readonly Dictionary<string, string?> Settings = new()
        {
            ["ConnectionStrings:DefaultConnection"] = ConnectionString,
            ["Jwt:Issuer"] = CustomWebApplicationFactory.TestJwtIssuer,
            ["Jwt:Audience"] = CustomWebApplicationFactory.TestJwtAudience,
            ["Jwt:SigningKey"] = CustomWebApplicationFactory.TestJwtSigningKey,
            ["Jwt:ExpiryMinutes"] = "60",
            ["BootstrapAdmin:Email"] = "",
            ["BootstrapAdmin:Password"] = "",
            ["BootstrapAdmin:DisplayName"] = ""
        };

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            foreach (var entry in Settings) builder.UseSetting(entry.Key, entry.Value);
            builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(Settings));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<AppDbContext>();
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.RemoveAll<DbContextOptions>();
                services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
                services.AddDbContext<AppDbContext>(fixture.ConfigureContext);
            });
        }

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureHostConfiguration(config => config.AddInMemoryCollection(Settings));
            return base.CreateHost(builder);
        }
    }

    public sealed class Scenario(SqlServerFixture fixture) : IAsyncDisposable
    {
        private readonly List<Guid> _userIds = [];
        private readonly List<Guid> _gameIds = [];

        public async Task<Guid> UserAsync(string role = "User", bool active = true)
        {
            var id = Guid.NewGuid();
            _userIds.Add(id);
            var email = $"sql-live-{id:N}@example.test";
            var user = new ApplicationUser
            {
                Id = id, UserName = email, Email = email, NormalizedUserName = email.ToUpperInvariant(),
                NormalizedEmail = email.ToUpperInvariant(), DisplayName = "SQL Live Fixture", IsActive = active,
                SecurityStamp = Guid.NewGuid().ToString("N"), ConcurrencyStamp = Guid.NewGuid().ToString("N")
            };
            await using var db = fixture.NewContext();
            db.AddRange(user, new IdentityUserRole<Guid> { UserId = id, RoleId = fixture._roles.Single(entry => entry.Name == role).Id });
            await db.SaveChangesAsync();
            return id;
        }

        public async Task<Guid> GameAsync(decimal price = 19.99m)
        {
            var game = new Game
            {
                Id = Guid.NewGuid(), Title = "SQL Live Fixture", Price = price, DiscountPercent = 25,
                Status = GameStatus.Published
            };
            game.Slug = $"sql-live-{game.Id:N}";
            _gameIds.Add(game.Id);
            await using var db = fixture.NewContext();
            db.Games.Add(game);
            await db.SaveChangesAsync();
            return game.Id;
        }

        public HttpClient Client(Guid userId)
        {
            Assert.Contains(userId, _userIds);
            var client = fixture._factory!.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false
            });
            client.Timeout = TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", IntegrationTestHelper.CreateTestJwtToken(userId));
            return client;
        }

        public async ValueTask DisposeAsync()
        {
            var users = _userIds.ToArray();
            var games = _gameIds.ToArray();
            await using var db = fixture.NewContext();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            var orders = db.Orders.Where(order => users.Contains(order.UserId));
            var items = db.OrderItems.Where(item => games.Contains(item.GameId) && orders.Any(order => order.Id == item.OrderId));
            var orderIds = items.Select(item => item.OrderId);
            await db.UserLibraries.Where(entry => users.Contains(entry.UserId) && games.Contains(entry.GameId) &&
                items.Any(item => item.Id == entry.OrderItemId)).ExecuteDeleteAsync();
            await db.Payments.Where(payment => orderIds.Contains(payment.OrderId)).ExecuteDeleteAsync();
            // Giữ ID của order trước khi xóa OrderItems để giới hạn cleanup đúng các order của kịch bản.
            var ownedOrders = await orderIds.Distinct().ToArrayAsync();
            await items.ExecuteDeleteAsync();
            await orders.Where(order => ownedOrders.Contains(order.Id)).ExecuteDeleteAsync();
            await db.UserRoles.Where(entry => users.Contains(entry.UserId) &&
                (entry.RoleId == fixture._roles[0].Id || entry.RoleId == fixture._roles[1].Id)).ExecuteDeleteAsync();
            await db.Users.Where(user => users.Contains(user.Id)).ExecuteDeleteAsync();
            await db.Games.Where(game => games.Contains(game.Id)).ExecuteDeleteAsync();
            await transaction.CommitAsync();
        }
    }
}
