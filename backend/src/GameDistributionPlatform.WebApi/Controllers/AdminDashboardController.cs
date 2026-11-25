using GameDistributionPlatform.Application.DTOs;
using GameDistributionPlatform.Application.Interfaces;
using GameDistributionPlatform.Infrastructure.Data.Seeding;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameDistributionPlatform.WebApi.Controllers;

[ApiController]
[Authorize(Roles = SeedConstants.AppRoles.Admin)]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
[Route("api/v1/admin/dashboard")]
public sealed class AdminDashboardController(IAdminReportingService reports) : ControllerBase
{
    [HttpGet("summary")]
    public Task<DashboardSummaryResponse> GetSummary(CancellationToken cancellationToken) => reports.GetDashboardAsync(cancellationToken);
}
