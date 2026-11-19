using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using GameDistributionPlatform.Infrastructure.Data;
using GameDistributionPlatform.Infrastructure.Identity;
using GameDistributionPlatform.WebApi.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameDistributionPlatform.WebApi.Tests.Jwt;

public sealed class JwtAuthenticationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public JwtAuthenticationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<ApplicationUser> CreateActiveUserAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = email,
            UserName = email,
            DisplayName = "Test User",
            IsActive = true
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task Authenticate_WithValidToken_Returns200Ok()
    {
        // Arrange
        var user = await CreateActiveUserAsync($"jwt_valid_{Guid.NewGuid():N}@example.com");
        var token = IntegrationTestHelper.CreateTestJwtToken(user.Id, user.Email, role: "User");

        // Act
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Authenticate_WithMalformedToken_Returns401Unauthorized()
    {
        // Act
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "this.is.not.a.valid.jwt.token");
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Authenticate_WithInvalidSignature_Returns401Unauthorized()
    {
        // Arrange - Ký bằng key khác hoàn toàn
        var wrongKey = Convert.ToBase64String(Encoding.UTF8.GetBytes("AnotherDifferentSigningKeyThatIsNotTheFactoryKey123!"));
        var user = await CreateActiveUserAsync($"jwt_wrongsig_{Guid.NewGuid():N}@example.com");
        var token = IntegrationTestHelper.CreateTestJwtToken(user.Id, user.Email, signingKey: wrongKey);

        // Act
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Authenticate_WithExpiredToken_Returns401Unauthorized()
    {
        // Arrange - Expiration nằm trong quá khứ 10 phút (không dùng Thread.Sleep)
        var user = await CreateActiveUserAsync($"jwt_expired_{Guid.NewGuid():N}@example.com");
        var token = IntegrationTestHelper.CreateTestJwtToken(user.Id, user.Email, expires: DateTime.UtcNow.AddMinutes(-10));

        // Act
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Authenticate_WithInvalidIssuer_Returns401Unauthorized()
    {
        // Arrange
        var user = await CreateActiveUserAsync($"jwt_badissuer_{Guid.NewGuid():N}@example.com");
        var token = IntegrationTestHelper.CreateTestJwtToken(user.Id, user.Email, issuer: "https://untrusted-issuer.com");

        // Act
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Authenticate_WithInvalidAudience_Returns401Unauthorized()
    {
        // Arrange
        var user = await CreateActiveUserAsync($"jwt_badaud_{Guid.NewGuid():N}@example.com");
        var token = IntegrationTestHelper.CreateTestJwtToken(user.Id, user.Email, audience: "WrongAudience");

        // Act
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Authenticate_WithMissingSubClaim_Returns401Unauthorized()
    {
        // Arrange - Token không có claim sub
        var token = IntegrationTestHelper.CreateTestJwtToken(includeSub: false);

        // Act
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Authenticate_WithMissingExpirationClaim_Returns401Unauthorized()
    {
        // Arrange - Token hợp lệ về chữ ký nhưng không có claim exp
        var user = await CreateActiveUserAsync($"jwt_noexp_{Guid.NewGuid():N}@example.com");
        var token = IntegrationTestHelper.CreateTestJwtToken(
            user.Id,
            user.Email,
            includeExpiration: false);
        var parsedToken = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.False(parsedToken.Payload.TryGetValue("exp", out _));

        // Act
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Authenticate_WithInvalidGuidSubClaim_Returns401Unauthorized()
    {
        // Arrange - Token có sub claim nhưng không phải Guid
        var token = IntegrationTestHelper.CreateTestJwtToken(includeSub: true, customSubValue: "not-a-valid-guid");

        // Act
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Authenticate_WhenUserDoesNotExistInDatabase_Returns401Unauthorized()
    {
        // Arrange - Token hợp lệ nhưng userId không tồn tại trong DB
        var nonExistentUserId = Guid.NewGuid();
        var token = IntegrationTestHelper.CreateTestJwtToken(userId: nonExistentUserId, email: "ghost@example.com");

        // Act
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Authenticate_WhenUserIsInactiveInDatabase_Returns401Unauthorized()
    {
        // Arrange - Tạo user có IsActive = false
        var user = await CreateActiveUserAsync($"jwt_inactive_{Guid.NewGuid():N}@example.com");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var dbUser = await db.Users.FirstAsync(u => u.Id == user.Id);
            dbUser.IsActive = false;
            await db.SaveChangesAsync();
        }

        var token = IntegrationTestHelper.CreateTestJwtToken(userId: user.Id, email: user.Email);

        // Act
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

