using GameDistributionPlatform.Application.DTOs;
using GameDistributionPlatform.Application.Interfaces;
using GameDistributionPlatform.Infrastructure.Data.Seeding;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameDistributionPlatform.WebApi.Controllers;

// Các endpoint quản trị đơn hàng chỉ có quyền đọc và dùng cùng quy tắc Admin của Catalog.
[ApiController]
[Authorize(Roles = SeedConstants.AppRoles.Admin)]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
[Route("api/v1/admin/orders")]
public sealed class AdminOrdersController(IAdminReportingService reports) : ControllerBase
{
    [HttpGet]
    public Task<PageResponse<AdminOrderSummaryResponse>> GetAll([FromQuery] AdminOrderQuery query, CancellationToken cancellationToken) =>
        reports.GetOrdersAsync(query, cancellationToken);

    [HttpGet("{id:guid}")]
    public Task<AdminOrderDetailResponse> GetById(Guid id, CancellationToken cancellationToken) => reports.GetOrderAsync(id, cancellationToken);
}
