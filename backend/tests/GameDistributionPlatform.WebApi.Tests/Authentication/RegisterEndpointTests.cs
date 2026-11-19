using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GameDistributionPlatform.Application.DTOs;
using GameDistributionPlatform.Infrastructure.Data.Seeding;
using GameDistributionPlatform.WebApi.Tests.Fixtures;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace GameDistributionPlatform.WebApi.Tests.Authentication;

public sealed class RegisterEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public RegisterEndpointTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Register_WithValidData_Returns201CreatedAndUserInfoResponse()
    {
        // Arrange
        var request = new RegisterRequest
        {
            Email = $"validuser_{Guid.NewGuid():N}@example.com",
            Password = "Password123!",
            ConfirmPassword = "Password123!",
            DisplayName = "Valid Player"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/auth/register", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var userInfo = await response.Content.ReadFromJsonAsync<UserInfoResponse>();
        Assert.NotNull(userInfo);
        Assert.NotEqual(Guid.Empty, userInfo.Id);
        Assert.Equal(request.Email, userInfo.Email);
        Assert.Equal("Valid Player", userInfo.DisplayName);
        Assert.Single(userInfo.Roles);
        Assert.Contains(SeedConstants.AppRoles.User, userInfo.Roles);

        // Đảm bảo không chứa trường password/hash trong response body
        var rawContent = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Password123!", rawContent);
        Assert.DoesNotContain("passwordHash", rawContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("accessToken", rawContent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Register_WithDuplicateEmail_Returns409ConflictProblemDetails()
    {
        // Arrange - Đăng ký user đầu tiên thành công
        var email = $"duplicate_{Guid.NewGuid():N}@example.com";
        var firstRequest = new RegisterRequest
        {
            Email = email,
            Password = "Password123!",
            ConfirmPassword = "Password123!",
            DisplayName = "First User"
        };
        var firstResponse = await _client.PostAsJsonAsync("/api/v1/auth/register", firstRequest);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

        // Act - Đăng ký lần 2 với cùng email
        var duplicateRequest = new RegisterRequest
        {
            Email = email,
            Password = "Password123!",
            ConfirmPassword = "Password123!",
            DisplayName = "Second User"
        };
        var duplicateResponse = await _client.PostAsJsonAsync("/api/v1/auth/register", duplicateRequest);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);
        Assert.Equal("application/problem+json", duplicateResponse.Content.Headers.ContentType?.MediaType);

        var problemDetails = await IntegrationTestHelper.ReadProblemDetailsAsync(duplicateResponse);
        Assert.NotNull(problemDetails);
        Assert.Equal(StatusCodes.Status409Conflict, problemDetails.Status);
        Assert.Equal("Conflict", problemDetails.Title);
        Assert.Contains("already registered", problemDetails.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("not-an-email", "Password123!", "Password123!", "Name", "The Email field is not a valid e-mail address.")]
    [InlineData("", "Password123!", "Password123!", "Name", "The Email field is required.")]
    [InlineData("test@example.com", "Password123!", "MismatchPass456!", "Name", "'ConfirmPassword' and 'Password' do not match.")]
    [InlineData("test@example.com", "short", "short", "Name", "The field Password must be a string or array type with a minimum length of '8'.")]
    [InlineData("test@example.com", "Password123!", "Password123!", "", "The DisplayName field is required.")]
    public async Task Register_WithInvalidDtoFields_Returns400BadRequestValidationProblemDetails(
        string email,
        string password,
        string confirmPassword,
        string displayName,
        string expectedErrorSnippet)
    {
        // Arrange
        var request = new RegisterRequest
        {
            Email = email,
            Password = password,
            ConfirmPassword = confirmPassword,
            DisplayName = displayName
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/auth/register", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var validationProblem = await IntegrationTestHelper.ReadValidationProblemDetailsAsync(response);
        Assert.NotNull(validationProblem);
        Assert.Equal(StatusCodes.Status400BadRequest, validationProblem.Status);
        Assert.NotEmpty(validationProblem.Errors);

        var rawContent = await response.Content.ReadAsStringAsync();
        Assert.Contains(expectedErrorSnippet, rawContent);
    }
}
