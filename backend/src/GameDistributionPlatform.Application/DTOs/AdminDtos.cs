using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using GameDistributionPlatform.Domain.Enums;

namespace GameDistributionPlatform.Application.DTOs;

public abstract class AdminListQuery
{
    [Range(1, 1_000_000, ErrorMessage = "Số trang phải từ 1 đến 1.000.000.")]
    public int PageNumber { get; init; } = 1;

    [Range(1, 50, ErrorMessage = "Mỗi trang chỉ được chứa từ 1 đến 50 mục.")]
    public int PageSize { get; init; } = 12;

    [StringLength(200, ErrorMessage = "Từ khóa tối đa 200 ký tự.")]
    public string? Search { get; init; }
}

public sealed class AdminOrderQuery : AdminListQuery
{
    [EnumDataType(typeof(OrderStatus), ErrorMessage = "Trạng thái đơn hàng không hợp lệ.")]
    public OrderStatus? Status { get; init; }

    [EnumDataType(typeof(PaymentStatus), ErrorMessage = "Trạng thái thanh toán không hợp lệ.")]
    public PaymentStatus? PaymentStatus { get; init; }

    public bool OldestFirst { get; init; }
}

public sealed class AdminUserQuery : AdminListQuery
{
    public bool? IsActive { get; init; }

    [RegularExpression("(?i)^(Admin|User)$", ErrorMessage = "Chỉ hỗ trợ lọc theo vai trò Admin hoặc User.")]
    public string? Role { get; init; }
}

public sealed class UpdateAdminUserStatusRequest
{
    [Required(ErrorMessage = "Vui lòng chọn trạng thái tài khoản.")]
    public bool? IsActive { get; init; }
}

public sealed record DashboardGameCounts(int Total, int Published, int Draft, int Archived);
public sealed record DashboardUserCounts(int Total, int Active, int Inactive);
public sealed record DashboardOrderCounts(int Total, int Pending, int Paid, int Cancelled);

public sealed record AdminRevenueResponse(
    string Currency,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] decimal Amount);

public sealed record AdminDailyRevenueResponse(
    DateOnly Date, string Currency,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] decimal Amount);

public sealed record DashboardSummaryResponse(
    DashboardGameCounts Games, int TotalCategories, DashboardUserCounts Users, DashboardOrderCounts Orders,
    IReadOnlyList<AdminRevenueResponse> Revenue, DateOnly RevenueStartDate,
    IReadOnlyList<AdminDailyRevenueResponse> DailyRevenue, IReadOnlyList<AdminRecentOrderResponse> RecentOrders,
    int UnverifiedPaidOrders, int RevenueOrdersMissingPaidAt);

public sealed record AdminRecentOrderResponse(
    Guid Id, string OrderCode, OrderStatus Status, DateTime CreatedAt, string Currency,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] decimal TotalAmount);

public sealed record AdminOrderSummaryResponse(
    Guid Id, string OrderCode, string CustomerName, string? CustomerEmail, OrderStatus Status,
    PaymentStatus? PaymentStatus, DateTime CreatedAt, string Currency,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] decimal TotalAmount);

public sealed record AdminOrderItemResponse(
    Guid GameId, string GameTitle, string? CoverImageUrl,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] decimal UnitPrice);

public sealed record AdminPaymentResponse(
    int AttemptNumber, PaymentStatus Status, PaymentMethod Method, string Currency, DateTime CreatedAt, DateTime? CompletedAt,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] decimal Amount);

public sealed record AdminOrderDetailResponse(
    AdminOrderSummaryResponse Order, DateTime? PaidAt, IReadOnlyList<AdminOrderItemResponse> Items,
    IReadOnlyList<AdminPaymentResponse> Payments)
{
    // Dùng giá snapshot trên từng mục đơn hàng để tính phần giảm giá tại lúc đặt.
    [JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)]
    public decimal OriginalAmount => Items.Sum(item => item.UnitPrice);

    [JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)]
    public decimal DiscountAmount => OriginalAmount - Order.TotalAmount;
}

public sealed record AdminUserSummaryResponse(
    Guid Id, string DisplayName, string? Email, bool IsActive, DateTime CreatedAt, IReadOnlyList<string> Roles);

public sealed record AdminUserDetailResponse(AdminUserSummaryResponse User, int TotalOrders, int OwnedGames);
