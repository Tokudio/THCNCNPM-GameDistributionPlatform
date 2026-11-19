using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GameDistributionPlatform.Application.DTOs;
using GameDistributionPlatform.Infrastructure.Data.Seeding;
using GameDistributionPlatform.WebApi.Tests.Fixtures;
using Xunit;

namespace GameDistributionPlatform.WebApi.Tests.Authentication;

public sealed class AuthenticationFlowTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public AuthenticationFlowTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task EndToEndFlow_RegisterThenLoginThenGetCurrentUser_SucceedsWithExactData()
    {
        // === BƯỚC 1: REGISTER ===
        var email = $"e2e_{Guid.NewGuid():N}@example.com";
        var password = "SuperSecretPassword123!";
        var displayName = "Pro Gamer E2E";

        var registerRequest = new RegisterRequest
        {
            Email = email,
            Password = password,
            ConfirmPassword = password,
            DisplayName = displayName
        };

        var registerResponse = await _client.PostAsJsonAsync("/api/v1/auth/register", registerRequest);
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);

        var registeredUser = await registerResponse.Content.ReadFromJsonAsync<UserInfoResponse>();
        Assert.NotNull(registeredUser);
        Assert.NotEqual(Guid.Empty, registeredUser.Id);
        Assert.Equal(email, registeredUser.Email);
        Assert.Equal(displayName, registeredUser.DisplayName);
        Assert.Single(registeredUser.Roles);
        Assert.Equal(SeedConstants.AppRoles.User, registeredUser.Roles[0]);

        // === BƯỚC 2: LOGIN ===
        var loginRequest = new LoginRequest
        {
            Email = email,
            Password = password
        };

        var loginResponse = await _client.PostAsJsonAsync("/api/v1/auth/login", loginRequest);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var authResponse = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(authResponse);
        Assert.False(string.IsNullOrWhiteSpace(authResponse.AccessToken));
        Assert.Equal("Bearer", authResponse.TokenType);
        Assert.True(authResponse.ExpiresAt > DateTimeOffset.UtcNow);

        // === BƯỚC 3: GET CURRENT USER (/api/v1/auth/me) ===
        using var meRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        meRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authResponse.AccessToken);

        var meResponse = await _client.SendAsync(meRequest);
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);

        var currentUser = await meResponse.Content.ReadFromJsonAsync<UserInfoResponse>();
        Assert.NotNull(currentUser);
        Assert.Equal(registeredUser.Id, currentUser.Id);
        Assert.Equal(email, currentUser.Email);
        Assert.Equal(displayName, currentUser.DisplayName);
        Assert.Single(currentUser.Roles);
        Assert.Equal(SeedConstants.AppRoles.User, currentUser.Roles[0]);
    }
}

