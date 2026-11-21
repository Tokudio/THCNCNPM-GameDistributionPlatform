using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GameDistributionPlatform.Application.DTOs;
using GameDistributionPlatform.Domain.Enums;
using GameDistributionPlatform.Infrastructure.Data;
using GameDistributionPlatform.Infrastructure.Data.Seeding;
using GameDistributionPlatform.Infrastructure.Identity;
using GameDistributionPlatform.WebApi.Tests.Fixtures;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameDistributionPlatform.WebApi.Tests.Catalog;

public sealed class CatalogEndpointTests : IDisposable
{
    private const string Categories = "/api/v1/categories";
    private const string AdminCategories = "/api/v1/admin/categories";
    private const string Games = "/api/v1/games";
    private const string AdminGames = "/api/v1/admin/games";
    private readonly CustomWebApplicationFactory _factory = new();

    [Theory]
    [InlineData("GET", AdminCategories)]
    [InlineData("POST", Categories)]
    [InlineData("PUT", Categories + "/{id}")]
    [InlineData("DELETE", Categories + "/{id}")]
    [InlineData("GET", AdminGames)]
    [InlineData("GET", AdminGames + "/{id}")]
    [InlineData("POST", AdminGames)]
    [InlineData("PUT", AdminGames + "/{id}")]
    [InlineData("PATCH", AdminGames + "/{id}/status")]
    public async Task AdminEndpointsReturn401ForAnonymousAnd403ForUser(string method, string path)
    {
        using var anonymous = _factory.CreateClient();
        using var user = await SignInAsync(SeedConstants.AppRoles.User);
        var url = path.Replace("{id}", Guid.NewGuid().ToString());
        using var anonymousRequest = CreateRequest(method, url);
        using var anonymousResponse = await anonymous.SendAsync(anonymousRequest);
        await AssertProblemAsync(anonymousResponse, HttpStatusCode.Unauthorized);
        Assert.Contains(anonymousResponse.Headers.WwwAuthenticate, value => value.Scheme == "Bearer");

        using var userRequest = CreateRequest(method, url);
        using var userResponse = await user.SendAsync(userRequest);
        await AssertProblemAsync(userResponse, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CategoryCrudUsesAdminJwtAndPublicReadExcludesInactiveRecords()
    {
        using var admin = await SignInAsync(SeedConstants.AppRoles.Admin);
        using var guest = _factory.CreateClient();
        var seeded = await guest.GetFromJsonAsync<CategoryResponse[]>(Categories);
        Assert.NotNull(seeded);
        Assert.Contains(seeded, item => item.Slug == "action" && item.IsActive);

        var input = new SaveCategoryRequest { Name = "Puzzle", Slug = "puzzle" };
        using var createdResponse = await admin.PostAsJsonAsync(Categories, input);
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var created = await createdResponse.Content.ReadFromJsonAsync<CategoryResponse>();
        Assert.NotNull(created);
        Assert.EndsWith($"{AdminCategories}/{created.Id}", createdResponse.Headers.Location?.ToString());
        Assert.Equal(created, await guest.GetFromJsonAsync<CategoryResponse>($"{Categories}/{created.Id}"));

        using var editedResponse = await admin.PutAsJsonAsync($"{Categories}/{created.Id}",
            new SaveCategoryRequest { Name = "Logic", Slug = input.Slug, IsActive = false });
        Assert.Equal(HttpStatusCode.OK, editedResponse.StatusCode);
        var adminCategories = await admin.GetFromJsonAsync<CategoryResponse[]>(AdminCategories);
        Assert.Contains(adminCategories!, category => category.Id == created.Id && category.Name == "Logic" && !category.IsActive);
        using var hidden = await guest.GetAsync($"{Categories}/{created.Id}");
        await AssertProblemAsync(hidden, HttpStatusCode.NotFound);

        using var deleted = await admin.DeleteAsync($"{Categories}/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using var missing = await admin.GetAsync($"{AdminCategories}/{created.Id}");
        await AssertProblemAsync(missing, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GamePutSyncsCollectionsAndPublishAndArchiveAffectPublicReads()
    {
        using var admin = await SignInAsync(SeedConstants.AppRoles.Admin);
        using var guest = _factory.CreateClient();
        var categories = await guest.GetFromJsonAsync<CategoryResponse[]>(Categories);
        Assert.NotNull(categories);
        var category = categories[0];
        using var createdResponse = await admin.PostAsJsonAsync(AdminGames, Request("my-game", [category.Id]));
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var draft = await createdResponse.Content.ReadFromJsonAsync<GameResponse>();
        Assert.NotNull(draft);
        Assert.Equal(GameStatus.Draft, draft.Status);
        Assert.EndsWith($"{AdminGames}/{draft.Id}", createdResponse.Headers.Location?.ToString());
        using var draftPublic = await guest.GetAsync($"{Games}/{draft.Id}");
        await AssertProblemAsync(draftPublic, HttpStatusCode.NotFound);

        using var updatedResponse = await admin.PutAsJsonAsync($"{AdminGames}/{draft.Id}",
            Request("my-game", [category.Id, categories[1].Id], [new() { MediaUrl = "https://example.com/screen.webp", DisplayOrder = 1 }]));
        Assert.Equal(HttpStatusCode.OK, updatedResponse.StatusCode);
        var updated = await updatedResponse.Content.ReadFromJsonAsync<GameResponse>();
        Assert.NotNull(updated);
        Assert.Equal(2, updated.Categories.Count);
        Assert.Single(updated.Media);
        using var publishedResponse = await admin.PatchAsJsonAsync($"{AdminGames}/{draft.Id}/status",
            new UpdateGameStatusRequest { Status = GameStatus.Published });
        Assert.Equal(HttpStatusCode.OK, publishedResponse.StatusCode);
        var published = await guest.GetFromJsonAsync<GameResponse>($"{Games}/{draft.Id}");
        Assert.Equal(GameStatus.Published, published?.Status);
        var page = await guest.GetFromJsonAsync<PageResponse<GameSummaryResponse>>($"{Games}?search=my&categoryId={category.Id}&pageSize=1");
        Assert.Equal(draft.Id, Assert.Single(page!.Items).Id);

        using var archiveResponse = await admin.PatchAsJsonAsync($"{AdminGames}/{draft.Id}/status",
            new UpdateGameStatusRequest { Status = GameStatus.Archived });
        Assert.Equal(HttpStatusCode.OK, archiveResponse.StatusCode);
        var publicPage = await guest.GetFromJsonAsync<PageResponse<GameSummaryResponse>>(Games);
        Assert.Empty(publicPage!.Items);
        var adminPage = await admin.GetFromJsonAsync<PageResponse<GameSummaryResponse>>($"{AdminGames}?status=Archived");
        Assert.Equal(draft.Id, Assert.Single(adminPage!.Items).Id);
    }

    [Fact]
    public async Task InvalidInputsAndConflictsUseProblemDetails()
    {
        using var admin = await SignInAsync(SeedConstants.AppRoles.Admin);
        var categories = await admin.GetFromJsonAsync<CategoryResponse[]>(AdminCategories);
        Assert.NotNull(categories);
        using var malformed = await admin.PostAsJsonAsync(AdminGames, new { title = "No slug", price = -1 });
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        Assert.Equal("application/problem+json", malformed.Content.Headers.ContentType?.MediaType);
        var validation = await malformed.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(validation);
        Assert.Equal(400, validation.Status);
        Assert.Contains("Slug", validation.Errors.Keys);
        Assert.Contains("Price", validation.Errors.Keys);

        using var invalidRelations = await admin.PostAsJsonAsync(AdminGames, Request("missing-category", [Guid.NewGuid()]));
        await AssertProblemAsync(invalidRelations, HttpStatusCode.BadRequest);
        using var created = await admin.PostAsJsonAsync(AdminGames, Request("unique-game"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var duplicate = await admin.PostAsJsonAsync(AdminGames, Request("unique-game"));
        await AssertProblemAsync(duplicate, HttpStatusCode.Conflict);
        using var categoryConflict = await admin.PostAsJsonAsync(Categories,
            new SaveCategoryRequest { Name = "Another name", Slug = categories[0].Slug });
        await AssertProblemAsync(categoryConflict, HttpStatusCode.Conflict);
        using var invalidQuery = await admin.GetAsync($"{AdminGames}?status=999&pageSize=100");
        await AssertProblemAsync(invalidQuery, HttpStatusCode.BadRequest);
        using var missing = await admin.PutAsJsonAsync($"{AdminGames}/{Guid.NewGuid()}", Request("updated"));
        await AssertProblemAsync(missing, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AssignedCategoryReturns409UntilGameIsUnlinked()
    {
        using var admin = await SignInAsync(SeedConstants.AppRoles.Admin);
        using var categoryResponse = await admin.PostAsJsonAsync(Categories,
            new SaveCategoryRequest { Name = "New category", Slug = "new-category" });
        var category = await categoryResponse.Content.ReadFromJsonAsync<CategoryResponse>();
        Assert.NotNull(category);
        using var gameResponse = await admin.PostAsJsonAsync(AdminGames, Request("linked-game", [category.Id]));
        var game = await gameResponse.Content.ReadFromJsonAsync<GameResponse>();
        Assert.NotNull(game);

        using var denied = await admin.DeleteAsync($"{Categories}/{category.Id}");
        await AssertProblemAsync(denied, HttpStatusCode.Conflict);
        using var updated = await admin.PutAsJsonAsync($"{AdminGames}/{game.Id}", Request("linked-game"));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        using var deleted = await admin.DeleteAsync($"{Categories}/{category.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        var remaining = await admin.GetFromJsonAsync<GameResponse>($"{AdminGames}/{game.Id}");
        Assert.Empty(remaining!.Categories);
    }

    [Fact]
    public async Task ExistingJwtLosesAccessWhenAdminRoleIsRevoked()
    {
        using var admin = await SignInAsync(SeedConstants.AppRoles.Admin);
        using var allowed = await admin.GetAsync(AdminGames);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = Assert.Single(await manager.GetUsersInRoleAsync(SeedConstants.AppRoles.Admin));
        Assert.True((await manager.RemoveFromRoleAsync(user, SeedConstants.AppRoles.Admin)).Succeeded);
        using var denied = await admin.GetAsync(AdminGames);
        await AssertProblemAsync(denied, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GameWritesIgnoreStatusAndOtherFieldsOutsideTheInputDto()
    {
        using var admin = await SignInAsync(SeedConstants.AppRoles.Admin);
        var spoofedId = Guid.NewGuid();
        var spoofedCreationDate = new DateTime(2000, 1, 1);
        var input = new
        {
            title = "Overposted game",
            slug = "overposted-game",
            description = "DTO boundary fixture",
            price = 7.25m,
            id = spoofedId,
            status = (int)GameStatus.Published,
            createdAt = spoofedCreationDate,
            demoDownloadUrl = "https://example.com/fixture"
        };
        using var createdResponse = await admin.PostAsJsonAsync(AdminGames, input);
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var created = await createdResponse.Content.ReadFromJsonAsync<GameResponse>();
        Assert.NotNull(created);
        Assert.NotEqual(spoofedId, created.Id);
        Assert.NotEqual(spoofedCreationDate, created.CreatedAt);
        Assert.Equal(GameStatus.Draft, created.Status);

        using var updatedResponse = await admin.PutAsJsonAsync($"{AdminGames}/{created.Id}", input);
        Assert.Equal(HttpStatusCode.OK, updatedResponse.StatusCode);
        var updated = await updatedResponse.Content.ReadFromJsonAsync<GameResponse>();
        Assert.NotNull(updated);
        Assert.Equal(created.Id, updated.Id);
        Assert.Equal(created.CreatedAt, updated.CreatedAt);
        Assert.Equal(GameStatus.Draft, updated.Status);

        using var scope = _factory.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Games.SingleAsync();
        Assert.Null(stored.DemoDownloadUrl);
    }

    public void Dispose() => _factory.Dispose();

    private async Task<HttpClient> SignInAsync(string role)
    {
        var client = _factory.CreateClient();
        const string password = "CatalogFixture!129";
        var email = $"catalog-{Guid.NewGuid():N}@example.com";
        using (var scope = _factory.Services.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = email, Email = email, DisplayName = "Catalog fixture", IsActive = true };
            Assert.True((await manager.CreateAsync(user, password)).Succeeded);
            Assert.True((await manager.AddToRoleAsync(user, role)).Succeeded);
        }

        using var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest { Email = email, Password = password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var credentials = await login.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(credentials);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", credentials.AccessToken);
        return client;
    }

    private static HttpRequestMessage CreateRequest(string method, string path)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method is "POST" or "PUT" or "PATCH") request.Content = JsonContent.Create(new { });
        return request;
    }

    private static SaveGameRequest Request(string slug, Guid[]? categories = null, SaveGameMediaRequest[]? media = null) =>
        new() { Title = slug, Slug = slug, Description = "Catalog integration fixture", Price = 5.5m, CategoryIds = categories ?? [], Media = media ?? [] };

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal((int)status, problem?.Status);
    }
}
