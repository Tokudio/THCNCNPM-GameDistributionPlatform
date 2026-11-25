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
[Route("api/v1/library/my")]
public sealed class LibraryController(IUserLibraryService library) : ControllerBase
{
    [HttpGet]
    public Task<PageResponse<LibraryGameResponse>> Get([FromQuery] LibraryQuery query, CancellationToken cancellationToken) =>
        library.GetMyLibraryAsync(UserId(), query, cancellationToken);

    [HttpGet("{gameId:guid}")]
    public Task<LibraryGameDetailResponse> GetGame(Guid gameId, CancellationToken cancellationToken) =>
        library.GetMyGameAsync(UserId(), gameId, cancellationToken);

    private Guid UserId() => Guid.TryParse(User.FindFirstValue(JwtRegisteredClaimNames.Sub), out var userId)
        ? userId : throw new UnauthorizedException("Invalid user identifier in authentication token.");
}
