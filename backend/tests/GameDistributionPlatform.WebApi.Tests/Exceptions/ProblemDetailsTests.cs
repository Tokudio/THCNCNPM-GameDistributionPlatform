using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GameDistributionPlatform.Application.DTOs;
using GameDistributionPlatform.Application.Exceptions;
using GameDistributionPlatform.WebApi.Exceptions;
using GameDistributionPlatform.WebApi.Tests.Fixtures;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace GameDistributionPlatform.WebApi.Tests.Exceptions;

public sealed class ProblemDetailsTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ProblemDetailsTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ValidationError_Returns400WithCorrectValidationProblemDetailsStructure()
    {
        // Arrange - Request không hợp lệ
        var invalidRequest = new RegisterRequest
        {
            Email = "invalid-email",
            Password = "short",
            ConfirmPassword = "not-matching",
            DisplayName = ""
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/auth/register", invalidRequest);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(StatusCodes.Status400BadRequest, root.GetProperty("status").GetInt32());
        Assert.Equal("One or more validation errors occurred.", root.GetProperty("title").GetString());
        Assert.Equal("https://tools.ietf.org/html/rfc9110#section-15.5.1", root.GetProperty("type").GetString());
        Assert.Equal("/api/v1/auth/register", root.GetProperty("instance").GetString());
        Assert.True(root.TryGetProperty("traceId", out var traceId));
        Assert.False(string.IsNullOrWhiteSpace(traceId.GetString()));

        // Kiểm tra danh sách validation errors
        Assert.True(root.TryGetProperty("errors", out var errors));
        Assert.True(errors.TryGetProperty("Email", out _));
        Assert.True(errors.TryGetProperty("Password", out _));
        Assert.True(errors.TryGetProperty("DisplayName", out _));
    }

    [Fact]
    public async Task UnauthorizedChallenge_Returns401WithWwwAuthenticateHeaderAndProblemDetails()
    {
        // Act - Truy cập endpoint yêu cầu [Authorize] mà không gửi token
        var response = await _client.GetAsync("/api/v1/auth/me");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains(response.Headers.WwwAuthenticate, h => h.Scheme == "Bearer");

        var problemDetails = await IntegrationTestHelper.ReadProblemDetailsAsync(response);
        Assert.NotNull(problemDetails);
        Assert.Equal(StatusCodes.Status401Unauthorized, problemDetails.Status);
        Assert.Equal("Unauthorized", problemDetails.Title);
        Assert.Equal("https://tools.ietf.org/html/rfc9110#section-15.5.2", problemDetails.Type);
        Assert.Equal("/api/v1/auth/me", problemDetails.Instance);
        Assert.True(problemDetails.Extensions.ContainsKey("traceId"));
    }

    [Fact]
    public async Task ConflictError_Returns409ProblemDetailsStructure()
    {
        // Arrange - Đăng ký trước 1 user
        var email = $"conflict_pd_{Guid.NewGuid():N}@example.com";
        var reg = new RegisterRequest
        {
            Email = email,
            Password = "Password123!",
            ConfirmPassword = "Password123!",
            DisplayName = "User"
        };
        await _client.PostAsJsonAsync("/api/v1/auth/register", reg);

        // Act - Đăng ký lại cùng email để sinh ConflictException
        var response = await _client.PostAsJsonAsync("/api/v1/auth/register", reg);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problemDetails = await IntegrationTestHelper.ReadProblemDetailsAsync(response);
        Assert.NotNull(problemDetails);
        Assert.Equal(StatusCodes.Status409Conflict, problemDetails.Status);
        Assert.Equal("Conflict", problemDetails.Title);
        Assert.Equal("https://tools.ietf.org/html/rfc9110#section-15.5.10", problemDetails.Type);
        Assert.Equal("/api/v1/auth/register", problemDetails.Instance);
        Assert.True(problemDetails.Extensions.ContainsKey("traceId"));
    }

    [Fact]
    public async Task ForbiddenEvent_ExecutesOnForbiddenAndOutputs403ProblemDetails()
    {
        // Component Test cho OnForbidden event đã cấu hình trong Program.cs
        using var scope = _factory.Services.CreateScope();
        var optionsMonitor = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>();
        var jwtOptions = optionsMonitor.Get(JwtBearerDefaults.AuthenticationScheme);

        var httpContext = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider
        };
        httpContext.Request.Path = "/api/v1/admin/secret";
        httpContext.Response.Body = new MemoryStream();

        var authScheme = new AuthenticationScheme(JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler));
        var forbiddenContext = new ForbiddenContext(httpContext, authScheme, jwtOptions);

        Assert.NotNull(jwtOptions.Events?.OnForbidden);
        await jwtOptions.Events.OnForbidden(forbiddenContext);

        Assert.Equal(StatusCodes.Status403Forbidden, httpContext.Response.StatusCode);

        httpContext.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(httpContext.Response.Body);
        var responseBody = await reader.ReadToEndAsync();

        using var doc = JsonDocument.Parse(responseBody);
        var root = doc.RootElement;
        Assert.Equal(StatusCodes.Status403Forbidden, root.GetProperty("status").GetInt32());
        Assert.Equal("Forbidden", root.GetProperty("title").GetString());
        Assert.Equal("You do not have permission to access this resource.", root.GetProperty("detail").GetString());
        Assert.Equal("https://tools.ietf.org/html/rfc9110#section-15.5.4", root.GetProperty("type").GetString());
    }

    [Fact]
    public async Task InternalServerError_DoesNotRevealStackTraceOrSensitiveInformation()
    {
        // Component Test cho GlobalExceptionHandler với Unhandled Exception
        using var scope = _factory.Services.CreateScope();
        var handler = new GlobalExceptionHandler(
            scope.ServiceProvider.GetService<IProblemDetailsService>()!,
            NullLogger<GlobalExceptionHandler>.Instance);

        var httpContext = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider
        };
        httpContext.Request.Path = "/api/v1/test/crash";
        httpContext.Response.Body = new MemoryStream();

        // Giả lập exception chứa thông tin nhạy cảm (Connection string, secret key, stack trace)
        var sensitiveException = new InvalidOperationException(
            "CRITICAL: SQL Server connection failed at Server=LAPTOP-UI1V2NBG;Password=MySecretPassword123! with SigningKey=SuperSecretBase64SigningKey!");

        // Act
        var handled = await handler.TryHandleAsync(httpContext, sensitiveException, CancellationToken.None);

        // Assert
        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError, httpContext.Response.StatusCode);

        httpContext.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(httpContext.Response.Body);
        var responseBody = await reader.ReadToEndAsync();

        using var doc = JsonDocument.Parse(responseBody);
        var root = doc.RootElement;

        Assert.Equal(StatusCodes.Status500InternalServerError, root.GetProperty("status").GetInt32());
        Assert.Equal("Internal Server Error", root.GetProperty("title").GetString());
        Assert.Equal("An internal server error occurred. Please contact support.", root.GetProperty("detail").GetString());

        // Tuyệt đối không rò rỉ thông tin nhạy cảm ra ngoài
        Assert.DoesNotContain("LAPTOP-UI1V2NBG", responseBody);
        Assert.DoesNotContain("MySecretPassword123!", responseBody);
        Assert.DoesNotContain("SuperSecretBase64SigningKey!", responseBody);
        Assert.DoesNotContain("StackTrace", responseBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("at GameDistributionPlatform", responseBody);
    }
}
