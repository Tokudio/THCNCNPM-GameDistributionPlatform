using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using GameDistributionPlatform.Application.DTOs;
using GameDistributionPlatform.Application.Exceptions;
using GameDistributionPlatform.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameDistributionPlatform.WebApi.Controllers;

[ApiController]
[Authorize]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
[Route("api/v1/orders")]
public sealed class OrdersController(IOrderService orders, ILogger<OrdersController> logger) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<OrderResponse>> Create(CreateOrderRequest request, CancellationToken cancellationToken)
    {
        var result = await orders.CreateAsync(UserId(), request.GameId, cancellationToken);
        if (!result.Created) return Ok(result.Order);
        logger.LogInformation("Created order {OrderId} with status {Status}.", result.Order.Id, result.Order.Status);
        return CreatedAtAction(nameof(Get), new { id = result.Order.Id }, result.Order);
    }

    [HttpGet("my")]
    public Task<PageResponse<OrderSummaryResponse>> GetMine([FromQuery] OrderQuery query, CancellationToken cancellationToken) =>
        orders.GetMineAsync(UserId(), query, cancellationToken);

    [HttpGet("{id:guid}")]
    public Task<OrderResponse> Get(Guid id, CancellationToken cancellationToken) =>
        orders.GetAsync(UserId(), id, cancellationToken);

    [HttpPost("{id:guid}/mock-payment")]
    public async Task<MockPaymentResponse> MockPayment(Guid id, MockPaymentRequest request, CancellationToken cancellationToken)
    {
        var result = await orders.CompleteMockPaymentAsync(UserId(), id, request.Outcome!.Value, cancellationToken);
        logger.LogInformation("Mock payment for order {OrderId} returned status {Status}.", result.OrderId, result.PaymentStatus);
        return result;
    }

    private Guid UserId() => Guid.TryParse(User.FindFirstValue(JwtRegisteredClaimNames.Sub), out var userId)
        ? userId : throw new UnauthorizedException("Invalid user identifier in authentication token.");
}
