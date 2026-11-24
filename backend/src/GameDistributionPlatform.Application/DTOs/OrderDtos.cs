using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using GameDistributionPlatform.Domain.Enums;

namespace GameDistributionPlatform.Application.DTOs;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateOrderRequest(Guid GameId);

public enum MockPaymentOutcome { Success, Failure }

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class MockPaymentRequest
{
    [Required, EnumDataType(typeof(MockPaymentOutcome))]
    public MockPaymentOutcome? Outcome { get; init; }
}

public sealed class OrderQuery
{
    [Range(1, 1_000_000)]
    public int PageNumber { get; init; } = 1;

    [Range(1, 50)]
    public int PageSize { get; init; } = 12;
}

public sealed record OrderItemResponse(
    Guid GameId, string GameTitle, string? CoverImageUrl, IReadOnlyList<string> Categories,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)]
    decimal UnitPrice);

public sealed record OrderResponse(
    Guid Id, string OrderCode, OrderStatus Status, string Currency, DateTime CreatedAt, DateTime? PaidAt,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)]
    decimal OriginalAmount,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)]
    decimal DiscountAmount,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)]
    decimal TotalAmount,
    IReadOnlyList<OrderItemResponse> Items, PaymentStatus? PaymentStatus, string? PaymentMessage,
    bool OwnershipGranted, bool MockPaymentAvailable);

public sealed record OrderSummaryResponse(
    Guid Id, string OrderCode, OrderStatus Status, string Currency, DateTime CreatedAt,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)]
    decimal TotalAmount, PaymentStatus? PaymentStatus);

public sealed record CreateOrderResult(OrderResponse Order, bool Created);

public sealed record MockPaymentResponse(
    Guid OrderId, OrderStatus OrderStatus, PaymentStatus PaymentStatus, string Currency,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)]
    decimal Amount, string Message);
