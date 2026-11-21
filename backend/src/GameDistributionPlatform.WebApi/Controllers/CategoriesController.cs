using GameDistributionPlatform.Application.DTOs;
using GameDistributionPlatform.Application.Interfaces;
using GameDistributionPlatform.Infrastructure.Data.Seeding;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameDistributionPlatform.WebApi.Controllers;

[ApiController]
[Route("api/v1/categories")]
public sealed class CategoriesController(ICategoryService categories) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CategoryResponse>>> GetAll(CancellationToken cancellationToken) =>
        Ok(await categories.GetAllAsync(false, cancellationToken));

    [AllowAnonymous]
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CategoryResponse>> GetById(Guid id, CancellationToken cancellationToken) =>
        await categories.GetByIdAsync(id, false, cancellationToken);

    [Authorize(Roles = SeedConstants.AppRoles.Admin)]
    [HttpGet("/api/v1/admin/categories")]
    public async Task<ActionResult<IReadOnlyList<CategoryResponse>>> GetAdminCategories(CancellationToken cancellationToken) =>
        Ok(await categories.GetAllAsync(true, cancellationToken));

    [Authorize(Roles = SeedConstants.AppRoles.Admin)]
    [HttpGet("/api/v1/admin/categories/{id:guid}")]
    public async Task<ActionResult<CategoryResponse>> GetAdminCategory(Guid id, CancellationToken cancellationToken) =>
        await categories.GetByIdAsync(id, true, cancellationToken);

    [Authorize(Roles = SeedConstants.AppRoles.Admin)]
    [HttpPost]
    [ProducesResponseType<CategoryResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<CategoryResponse>> Create(SaveCategoryRequest request, CancellationToken cancellationToken)
    {
        var created = await categories.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetAdminCategory), new { id = created.Id }, created);
    }

    [Authorize(Roles = SeedConstants.AppRoles.Admin)]
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<CategoryResponse>> Update(
        Guid id, SaveCategoryRequest request, CancellationToken cancellationToken) =>
        await categories.UpdateAsync(id, request, cancellationToken);

    [Authorize(Roles = SeedConstants.AppRoles.Admin)]
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await categories.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
