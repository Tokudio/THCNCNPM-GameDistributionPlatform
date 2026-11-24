using System.Data;
using GameDistributionPlatform.Application.DTOs;
using GameDistributionPlatform.Application.Exceptions;
using GameDistributionPlatform.Application.Interfaces;
using GameDistributionPlatform.Application.Orders;
using GameDistributionPlatform.Domain.Entities;
using GameDistributionPlatform.Domain.Enums;
using GameDistributionPlatform.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GameDistributionPlatform.Infrastructure.Services;

public sealed class OrderService(AppDbContext db, CheckoutAvailability availability) : IOrderService
{
    private const string FailureMessage = "The simulated payment failed. No money was charged.";
    private const string SuccessMessage = "The simulated payment succeeded. No money was charged.";

    public Task<CreateOrderResult> CreateAsync(Guid userId, Guid gameId, CancellationToken cancellationToken)
    {
        EnsureCheckoutAvailable();
        if (gameId == Guid.Empty) throw new BadRequestException("A valid game ID is required.");

        return WriteAsync(userId, async () =>
        {
            var game = await db.Games.AsNoTracking()
                .Include(game => game.GameCategories).ThenInclude(link => link.Category)
                .SingleOrDefaultAsync(game => game.Id == gameId && game.Status == GameStatus.Published, cancellationToken)
                ?? throw new NotFoundException("Published game was not found.");

            await EnsureNotOwnedAsync(userId, gameId, cancellationToken);
            var pending = await OrderDetails(userId)
                .Where(order => order.Status == OrderStatus.Pending && order.Items.Count == 1 &&
                    order.Items.Any(item => item.GameId == gameId))
                .OrderBy(order => order.CreatedAt).ThenBy(order => order.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (pending is not null) return new CreateOrderResult(ToResponse(pending), false);

            var total = OrderPricing.Total(game.Price, game.DiscountPercent);
            var now = DateTime.UtcNow;
            var order = new Order { UserId = userId, Currency = game.Currency, TotalAmount = total, CreatedAt = now };
            order.OrderCode = $"ORD-{order.Id:N}";
            var item = new OrderItem { GameId = game.Id, GameTitle = game.Title, UnitPrice = game.Price, CreatedAt = now };
            order.Items.Add(item);
            if (total == 0)
            {
                // A zero-total claim has a paid order and ownership, with no payment record.
                MarkPaid(order, item, now);
            }

            db.Orders.Add(order);
            await db.SaveChangesAsync(cancellationToken);
            return new CreateOrderResult(ToResponse(order, game), true);
        }, cancellationToken);
    }

    public async Task<OrderResponse> GetAsync(Guid userId, Guid orderId, CancellationToken cancellationToken)
    {
        var order = await OrderDetails(userId).SingleOrDefaultAsync(order => order.Id == orderId, cancellationToken)
            ?? throw new NotFoundException("Order was not found.");
        return ToResponse(order);
    }

    public async Task<PageResponse<OrderSummaryResponse>> GetMineAsync(
        Guid userId, OrderQuery query, CancellationToken cancellationToken)
    {
        CatalogInput.Validate(query);
        var orders = db.Orders.AsNoTracking().Where(order => order.UserId == userId);
        var count = await orders.CountAsync(cancellationToken);
        var items = await orders.OrderByDescending(order => order.CreatedAt).ThenBy(order => order.Id)
            .Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize)
            .Select(order => new OrderSummaryResponse(order.Id, order.OrderCode, order.Status, order.Currency,
                order.CreatedAt, order.TotalAmount, order.Payments.OrderByDescending(payment => payment.AttemptNumber)
                    .Select(payment => (PaymentStatus?)payment.Status).FirstOrDefault()))
            .ToListAsync(cancellationToken);
        return new PageResponse<OrderSummaryResponse>(query.PageNumber, query.PageSize, count,
            (int)Math.Ceiling((decimal)count / query.PageSize), items);
    }

    public Task<MockPaymentResponse> CompleteMockPaymentAsync(
        Guid userId, Guid orderId, MockPaymentOutcome outcome, CancellationToken cancellationToken)
    {
        EnsureCheckoutAvailable();
        if (!Enum.IsDefined(outcome)) throw new BadRequestException("Choose a valid mock payment outcome.");

        return WriteAsync(userId, async () =>
        {
            var order = await db.Orders.Where(order => order.UserId == userId && order.Id == orderId)
                .Include(order => order.Items).ThenInclude(item => item.Game)
                .Include(order => order.Payments).AsSplitQuery().SingleOrDefaultAsync(cancellationToken)
                ?? throw new NotFoundException("Order was not found.");
            var success = outcome == MockPaymentOutcome.Success;
            var orderStatus = success ? OrderStatus.Paid : OrderStatus.Cancelled;
            var paymentStatus = success ? PaymentStatus.Succeeded : PaymentStatus.Failed;
            var key = $"mock:{order.Id:N}";

            // The server assigns one immutable mock attempt per order. An identical replay gets its saved result.
            var existing = order.Payments.SingleOrDefault(payment => payment.IdempotencyKey == key);
            if (existing is not null && existing.Status == paymentStatus && order.Status == orderStatus)
            {
                return ToPaymentResponse(order, existing);
            }

            if (order.Status != OrderStatus.Pending || order.Payments.Count != 0 ||
                order.TotalAmount <= 0 || order.Items.Count != 1)
            {
                throw new ConflictException("This order cannot accept a new mock payment.");
            }

            var item = order.Items.Single();
            if (success && item.Game.Status != GameStatus.Published)
            {
                throw new ConflictException("This game is no longer available. The order was not charged.");
            }

            var now = DateTime.UtcNow;
            var payment = new Payment
            {
                OrderId = order.Id, Amount = order.TotalAmount, Currency = order.Currency,
                Method = PaymentMethod.Mock, Status = paymentStatus, AttemptNumber = 1, IdempotencyKey = key,
                TransactionReference = success ? key : null, FailureReason = success ? null : FailureMessage,
                CreatedAt = now, CompletedAt = now
            };
            if (success)
            {
                await EnsureNotOwnedAsync(userId, item.GameId, cancellationToken);
                db.UserLibraries.Add(MarkPaid(order, item, now));
            }
            else
            {
                order.Status = OrderStatus.Cancelled;
                order.UpdatedAt = now;
            }

            db.Payments.Add(payment);
            await db.SaveChangesAsync(cancellationToken);
            return ToPaymentResponse(order, payment);
        }, cancellationToken);
    }

    private void EnsureCheckoutAvailable()
    {
        if (!availability.MockPaymentAvailable) throw new CheckoutUnavailableException();
    }

    private async Task<T> WriteAsync<T>(Guid userId, Func<Task<T>> write, CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            // SQL Server serializes this user's checkout writes on their existing account row.
            // The SQLite test provider starts an immediate write transaction.
            var users = db.Database.IsSqlServer()
                ? db.Users.FromSqlInterpolated($"SELECT * FROM [AspNetUsers] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {userId}")
                : db.Users.Where(user => user.Id == userId);
            if (!await users.AnyAsync(user => user.IsActive, cancellationToken))
                throw new UnauthorizedException("User account not found or is inactive.");

            var result = await write();
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (Exception error) when (error.GetBaseException() is SqlException { Number: 1205 or 2601 or 2627 })
        {
            throw new ConflictException("The order changed during this operation. Reload and try again.");
        }
    }

    private async Task EnsureNotOwnedAsync(Guid userId, Guid gameId, CancellationToken cancellationToken)
    {
        if (await db.UserLibraries.AnyAsync(item => item.UserId == userId && item.GameId == gameId, cancellationToken))
            throw new GameAlreadyOwnedException();
    }

    private static UserLibrary MarkPaid(Order order, OrderItem item, DateTime now)
    {
        order.Status = OrderStatus.Paid;
        order.PaidAt = now;
        order.UpdatedAt = now;
        return item.UserLibrary = new UserLibrary
        {
            UserId = order.UserId, GameId = item.GameId, OrderItemId = item.Id, AcquiredAt = now, CreatedAt = now
        };
    }

    private IQueryable<Order> OrderDetails(Guid userId) => db.Orders.AsNoTracking()
        .Where(order => order.UserId == userId)
        .Include(order => order.Items).ThenInclude(item => item.Game).ThenInclude(game => game.GameCategories).ThenInclude(link => link.Category)
        .Include(order => order.Items).ThenInclude(item => item.UserLibrary)
        .Include(order => order.Payments)
        .AsSingleQuery();

    private OrderResponse ToResponse(Order order, Game? newGame = null)
    {
        var original = order.Items.Sum(item => item.UnitPrice);
        var payment = order.Payments.OrderByDescending(payment => payment.AttemptNumber).FirstOrDefault();
        var items = order.Items.OrderBy(item => item.Id).Select(item =>
        {
            var game = newGame ?? item.Game;
            return new OrderItemResponse(item.GameId, item.GameTitle, game.CoverImageUrl,
                game.GameCategories.Where(link => link.Category.IsActive).Select(link => link.Category.Name)
                    .OrderBy(name => name).ToArray(), item.UnitPrice);
        }).ToArray();
        return new OrderResponse(order.Id, order.OrderCode, order.Status, order.Currency, order.CreatedAt, order.PaidAt,
            original, original - order.TotalAmount, order.TotalAmount, items, payment?.Status, payment?.FailureReason,
            order.Items.Count > 0 && order.Items.All(item => item.UserLibrary is not null), availability.MockPaymentAvailable);
    }

    private static MockPaymentResponse ToPaymentResponse(Order order, Payment payment) =>
        new(order.Id, order.Status, payment.Status, payment.Currency, payment.Amount,
            payment.Status == PaymentStatus.Succeeded ? SuccessMessage : FailureMessage);
}
