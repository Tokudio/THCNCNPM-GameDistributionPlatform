using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GameDistributionPlatform.Application.DTOs;
using GameDistributionPlatform.WebApi.Tests.Fixtures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace GameDistributionPlatform.WebApi.Tests.Authentication;

public sealed class AuthRateLimitTests
{
    private const string Login = "/api/v1/auth/login";
    private const string Register = "/api/v1/auth/register";
    private const string Password = "FixturePassword123!";

    [Theory]
    [InlineData("Testing")]
    [InlineData("Production")]
    public async Task LoginReturnsProblemDetailsAndRetryAfterWhenQuotaEnds(string environment)
    {
        using var factory = new RateFactory(environment);
        using var client = factory.CreateClient();
        using var registration = await RegisterAsync(client);
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        var user = await registration.Content.ReadFromJsonAsync<UserInfoResponse>();
        Assert.NotNull(user);

        using var login = await client.PostAsJsonAsync(Login, new { user.Email, Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var session = await login.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(session);
        for (var index = 0; index < 4; index++)
        {
            using var failed = await client.PostAsJsonAsync(Login, new { user.Email, Password = "wrong" });
            Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
        }

        using var rejected = new HttpRequestMessage(HttpMethod.Post, Login) { Content = JsonContent.Create(new { user.Email, Password }) };
        rejected.Headers.TryAddWithoutValidation("X-Forwarded-For", "192.0.2.241");
        using var response = await client.SendAsync(rejected);
        await AssertRateLimitedAsync(response, 60);
        using var profile = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        profile.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        using var me = await client.SendAsync(profile);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task RegisterHasItsOwnQuotaAndAllowsValidRequests()
    {
        using var factory = new RateFactory("Testing");
        using var client = factory.CreateClient();
        for (var index = 0; index < 3; index++)
        {
            using var registration = await RegisterAsync(client);
            Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        }

        using var rejected = await RegisterAsync(client);
        await AssertRateLimitedAsync(rejected, 300);
        using var login = await client.PostAsJsonAsync(Login, new { Email = "unknown@example.test", Password });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    [Fact]
    public async Task ServerAssignedAddressesUseSeparatePartitions()
    {
        using var factory = new RateFactory("Testing", permits: "1");

        Assert.Equal(400, await SendAsync("192.0.2.20"));
        Assert.Equal(429, await SendAsync("::ffff:192.0.2.20"));
        // Có thể trùng bucket, nên kiểm tra nhiều IP fixture để tìm một bucket độc lập.
        var results = await Task.WhenAll(Enumerable.Range(21, 8).Select(index => SendAsync($"192.0.2.{index}")));
        Assert.Contains(400, results);
        Assert.Equal(429, await SendAsync("192.0.2.20"));

        async Task<int> SendAsync(string address)
        {
            var response = await factory.Server.SendAsync(context =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Parse(address);
                context.Request.Scheme = "https";
                context.Request.Method = "POST";
                context.Request.Path = Login;
                context.Request.ContentType = "application/json";
                context.Request.Body = new MemoryStream("{}"u8.ToArray());
            });
            return response.Response.StatusCode;
        }
    }

    [Theory]
    [InlineData("0")]
    [InlineData("abc")]
    public void InvalidRateConfigFailsStartup(string permits)
    {
        using var factory = new RateFactory("Testing", permits);
        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("AuthRateLimiting:Login", exception.Message);
    }

    private static Task<HttpResponseMessage> RegisterAsync(HttpClient client) =>
        client.PostAsJsonAsync(Register, new
        {
            Email = $"rate-{Guid.NewGuid():N}@example.test",
            DisplayName = "Fixture User",
            Password,
            ConfirmPassword = Password
        });

    private static async Task AssertRateLimitedAsync(HttpResponseMessage response, int maximumDelay)
    {
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var delay = int.Parse(response.Headers.GetValues("Retry-After").Single(), CultureInfo.InvariantCulture);
        Assert.InRange(delay, 1, maximumDelay);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(429, problem?.Status);
        Assert.Equal("Bạn đã gửi quá nhiều yêu cầu. Vui lòng thử lại sau.", problem?.Detail);
    }

    private sealed class RateFactory(string environment, string? permits = null) : CustomWebApplicationFactory
    {
        protected override string TestEnvironment => environment;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            if (permits is not null) builder.UseSetting("AuthRateLimiting:Login:PermitLimit", permits);
        }
    }
}
