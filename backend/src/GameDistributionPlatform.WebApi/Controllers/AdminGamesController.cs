using GameDistributionPlatform.Application.DTOs;
using GameDistributionPlatform.Application.Interfaces;
using GameDistributionPlatform.Infrastructure.Data.Seeding;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameDistributionPlatform.WebApi.Controllers;

[ApiController]
[Authorize(Roles = SeedConstants.AppRoles.Admin)]
[Route("api/v1/admin/games")]
public sealed class AdminGamesController(IGameService games) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PageResponse<GameSummaryResponse>>> GetAll(
        [FromQuery] AdminGameQuery query, CancellationToken cancellationToken) =>
        await games.GetAdminGamesAsync(query, cancellationToken);

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GameResponse>> GetById(Guid id, CancellationToken cancellationToken) =>
        await games.GetByIdAsync(id, true, cancellationToken);

    [HttpPost]
    [ProducesResponseType<GameResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<GameResponse>> Create(SaveGameRequest request, CancellationToken cancellationToken)
    {
        var created = await games.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<GameResponse>> Update(
        Guid id, SaveGameRequest request, CancellationToken cancellationToken) =>
        await games.UpdateAsync(id, request, cancellationToken);

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<GameResponse>> UpdateStatus(
        Guid id, UpdateGameStatusRequest request, CancellationToken cancellationToken) =>
        await games.UpdateStatusAsync(id, request, cancellationToken);
}
