using GameDistributionPlatform.Application.DTOs;
using GameDistributionPlatform.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameDistributionPlatform.WebApi.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/v1/games")]
public sealed class GamesController(IGameService games, CheckoutAvailability checkout) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PageResponse<GameSummaryResponse>>> GetAll(
        [FromQuery] GameQuery query, CancellationToken cancellationToken) =>
        await games.GetGamesAsync(query, cancellationToken);

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GameResponse>> GetById(Guid id, CancellationToken cancellationToken) =>
        (await games.GetByIdAsync(id, false, cancellationToken)) with { MockPaymentAvailable = checkout.MockPaymentAvailable };
}
