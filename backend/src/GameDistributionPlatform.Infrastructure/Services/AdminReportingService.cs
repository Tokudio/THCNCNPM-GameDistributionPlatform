using System.Data;
using GameDistributionPlatform.Application.DTOs;
using GameDistributionPlatform.Application.Exceptions;
using GameDistributionPlatform.Application.Interfaces;
using GameDistributionPlatform.Domain.Entities;
using GameDistributionPlatform.Domain.Enums;
using GameDistributionPlatform.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GameDistributionPlatform.Infrastructure.Services;

public sealed class AdminReportingService(AppDbContext db) : IAdminReportingService
{
    public async Task<DashboardSummaryResponse> GetDashboardAsync(CancellationToken cancellationToken)
    {
        // SQL gom nhóm trước khi trả dữ liệu để Dashboard chỉ đọc các số tổng hợp.
        var gameCounts = await db.Games.AsNoTracking().GroupBy(game => game.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.Status, row => row.Count, cancellationToken);
        var categories = await db.Categories.CountAsync(cancellationToken);
        var userCounts = await db.Users.AsNoTracking().GroupBy(user => user.IsActive)
            .Select(group => new { IsActive = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.IsActive, row => row.Count, cancellationToken);
        var orderCounts = await db.Orders.AsNoTracking().GroupBy(order => order.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.Status, row => row.Count, cancellationToken);

        // EXISTS xác minh đúng số tiền và loại tiền của đơn Paid. Mỗi đơn góp doanh thu từ một snapshot.
        var paid = db.Orders.AsNoTracking().Where(order => order.Status == OrderStatus.Paid)
            .Select(order => new
            {
                Order = order,
                Verified = order.TotalAmount == 0 || (order.TotalAmount > 0 && order.Payments.Any(payment =>
                    payment.Status == PaymentStatus.Succeeded && payment.Amount == order.TotalAmount && payment.Currency == order.Currency))
            });
        var unverifiedPaidOrders = await paid.CountAsync(row => !row.Verified, cancellationToken);
        var verified = paid.Where(row => row.Verified).Select(row => row.Order);
        var revenue = await verified.GroupBy(order => order.Currency).OrderBy(group => group.Key)
            .Select(group => new
            {
                Currency = group.Key,
                Amount = group.Sum(order => order.TotalAmount),
                MissingPaidAt = group.Count(order => order.PaidAt == null)
            })
            .ToListAsync(cancellationToken);

        var end = DateTime.UtcNow.Date.AddDays(1);
        var start = end.AddDays(-7);
        var daily = await verified.Where(order => order.PaidAt >= start && order.PaidAt < end)
            .GroupBy(order => new { Date = order.PaidAt!.Value.Date, order.Currency })
            .OrderBy(group => group.Key.Date).ThenBy(group => group.Key.Currency)
            .Select(group => new { group.Key.Date, group.Key.Currency, Amount = group.Sum(order => order.TotalAmount) })
            .ToListAsync(cancellationToken);
        var recent = await db.Orders.AsNoTracking().OrderByDescending(order => order.CreatedAt)
            .ThenByDescending(order => order.Id).Take(5)
            .Select(order => new AdminRecentOrderResponse(order.Id, order.OrderCode, order.Status,
                DateTime.SpecifyKind(order.CreatedAt, DateTimeKind.Utc), order.Currency, order.TotalAmount))
            .ToListAsync(cancellationToken);

        return new DashboardSummaryResponse(
            new DashboardGameCounts(gameCounts.Values.Sum(), gameCounts.GetValueOrDefault(GameStatus.Published),
                gameCounts.GetValueOrDefault(GameStatus.Draft), gameCounts.GetValueOrDefault(GameStatus.Archived)),
            categories, new DashboardUserCounts(userCounts.Values.Sum(), userCounts.GetValueOrDefault(true),
                userCounts.GetValueOrDefault(false)),
            new DashboardOrderCounts(orderCounts.Values.Sum(), orderCounts.GetValueOrDefault(OrderStatus.Pending),
                orderCounts.GetValueOrDefault(OrderStatus.Paid), orderCounts.GetValueOrDefault(OrderStatus.Cancelled)),
            revenue.Select(row => new AdminRevenueResponse(row.Currency, row.Amount)).ToArray(), DateOnly.FromDateTime(start),
            daily.Select(row => new AdminDailyRevenueResponse(DateOnly.FromDateTime(row.Date), row.Currency, row.Amount)).ToArray(),
            recent, unverifiedPaidOrders, revenue.Sum(row => row.MissingPaidAt));
    }

    public async Task<PageResponse<AdminOrderSummaryResponse>> GetOrdersAsync(AdminOrderQuery query, CancellationToken cancellationToken)
    {
        CatalogInput.Validate(query);
        var rows = OrderRows();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim().ToLowerInvariant();
            rows = Guid.TryParse(search, out var id)
                ? rows.Where(row => row.Order.Id == id)
                : rows.Where(row => row.Order.OrderCode.ToLower().Contains(search) || row.CustomerName.ToLower().Contains(search) ||
                    (row.CustomerEmail != null && row.CustomerEmail.ToLower().Contains(search)));
        }
        if (query.Status is { } status) rows = rows.Where(row => row.Order.Status == status);
        if (query.PaymentStatus is { } paymentStatus) rows = rows.Where(row => row.PaymentStatus == paymentStatus);

        // Dùng ID để giữ thứ tự ổn định giữa các trang khi nhiều đơn có cùng thời điểm tạo.
        var count = await rows.CountAsync(cancellationToken);
        var sorted = query.OldestFirst
            ? rows.OrderBy(row => row.Order.CreatedAt).ThenBy(row => row.Order.Id)
            : rows.OrderByDescending(row => row.Order.CreatedAt).ThenByDescending(row => row.Order.Id);
        var items = await Summaries(sorted.Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize))
            .ToListAsync(cancellationToken);
        return new PageResponse<AdminOrderSummaryResponse>(query.PageNumber, query.PageSize, count,
            (int)Math.Ceiling((decimal)count / query.PageSize), items);
    }

    public async Task<AdminOrderDetailResponse> GetOrderAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            // Giữ cùng mốc đọc cho ba truy vấn, kể cả khi checkout đang ghi trạng thái và thanh toán.
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var detail = await OrderRows().Where(row => row.Order.Id == id).Select(row => new AdminOrderDetailResponse(
                new AdminOrderSummaryResponse(row.Order.Id, row.Order.OrderCode, row.CustomerName, row.CustomerEmail,
                    row.Order.Status, row.PaymentStatus, DateTime.SpecifyKind(row.Order.CreatedAt, DateTimeKind.Utc),
                    row.Order.Currency, row.Order.TotalAmount),
                row.Order.PaidAt.HasValue ? DateTime.SpecifyKind(row.Order.PaidAt.Value, DateTimeKind.Utc) : null,
                row.Order.Items.OrderBy(item => item.Id).Select(item => new AdminOrderItemResponse(
                    item.GameId, item.GameTitle, item.Game.CoverImageUrl, item.UnitPrice)).ToArray(),
                row.Order.Payments.OrderByDescending(payment => payment.AttemptNumber).Select(payment => new AdminPaymentResponse(
                    payment.AttemptNumber, payment.Status, payment.Method, payment.Currency,
                    DateTime.SpecifyKind(payment.CreatedAt, DateTimeKind.Utc),
                    payment.CompletedAt.HasValue ? DateTime.SpecifyKind(payment.CompletedAt.Value, DateTimeKind.Utc) : null,
                    payment.Amount)).ToArray())).AsSplitQuery().SingleOrDefaultAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return detail ?? throw new NotFoundException("Không tìm thấy đơn hàng.");
        }
        catch (Exception error) when (error.GetBaseException() is SqlException { Number: 1205 })
        {
            throw new ConflictException("Dữ liệu đơn hàng đang được cập nhật. Vui lòng thử lại.");
        }
    }

    private IQueryable<OrderRow> OrderRows() =>
        from order in db.Orders.AsNoTracking()
        join user in db.Users.AsNoTracking() on order.UserId equals user.Id
        select new OrderRow
        {
            Order = order,
            CustomerName = user.DisplayName,
            CustomerEmail = user.Email,
            PaymentStatus = order.Payments.OrderByDescending(payment => payment.AttemptNumber)
                .Select(payment => (PaymentStatus?)payment.Status).FirstOrDefault()
        };

    private static IQueryable<AdminOrderSummaryResponse> Summaries(IQueryable<OrderRow> rows) => rows.Select(row =>
        new AdminOrderSummaryResponse(row.Order.Id, row.Order.OrderCode, row.CustomerName, row.CustomerEmail,
            row.Order.Status, row.PaymentStatus, DateTime.SpecifyKind(row.Order.CreatedAt, DateTimeKind.Utc),
            row.Order.Currency, row.Order.TotalAmount));

    private sealed class OrderRow
    {
        public required Order Order { get; init; }
        public required string CustomerName { get; init; }
        public string? CustomerEmail { get; init; }
        public PaymentStatus? PaymentStatus { get; init; }
    }
}
