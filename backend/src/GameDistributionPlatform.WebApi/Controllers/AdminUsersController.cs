using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using GameDistributionPlatform.Application.DTOs;
using GameDistributionPlatform.Application.Exceptions;
using GameDistributionPlatform.Application.Interfaces;
using GameDistributionPlatform.Infrastructure.Data.Seeding;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameDistributionPlatform.WebApi.Controllers;

[ApiController]
[Authorize(Roles = SeedConstants.AppRoles.Admin)]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
[Route("api/v1/admin/users")]
public sealed class AdminUsersController(IAdminUserService users) : ControllerBase
{
    [HttpGet]
    public Task<PageResponse<AdminUserSummaryResponse>> GetAll([FromQuery] AdminUserQuery query, CancellationToken cancellationToken) =>
        users.GetUsersAsync(query, cancellationToken);

    [HttpGet("{id:guid}")]
    public Task<AdminUserDetailResponse> GetById(Guid id, CancellationToken cancellationToken) => users.GetUserAsync(id, cancellationToken);

    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> UpdateStatus(Guid id, UpdateAdminUserStatusRequest request, CancellationToken cancellationToken)
    {
        // Tác nhân lấy từ JWT đã xác minh, DTO chỉ nhận trạng thái cần cập nhật.
        if (!Guid.TryParse(User.FindFirstValue(JwtRegisteredClaimNames.Sub), out var actorId))
            throw new UnauthorizedException("Phiên quản trị không còn hiệu lực.");
        await users.UpdateStatusAsync(actorId, id, request.IsActive!.Value, cancellationToken);
        return NoContent();
    }
}
