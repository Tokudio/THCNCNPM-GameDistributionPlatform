using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using GameDistributionPlatform.Application.DTOs;
using GameDistributionPlatform.Infrastructure.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Xunit;
using JwtServiceImplementation = GameDistributionPlatform.Infrastructure.Services.JwtService;
using MicrosoftOptions = Microsoft.Extensions.Options.Options;

namespace GameDistributionPlatform.Infrastructure.Tests.JwtService;

public sealed class JwtServiceTests
{
    private const string Issuer = "https://issuer.test";
    private const string Audience = "gamedistribution-tests";
    private const int ExpiryMinutes = 60;

    [Fact]
    public void GenerateToken_ValidOptions_ReturnsTokenAcceptedByValidationParameters()
    {
        var options = CreateValidOptions();
        var service = CreateService(options);

        var response = service.GenerateToken(CreateUser());
        var handler = CreateTokenHandler();
        var principal = handler.ValidateToken(
            response.AccessToken,
            CreateValidationParameters(options),
            out var validatedToken);

        Assert.IsType<JwtSecurityToken>(validatedToken);
        Assert.Equal(Issuer, validatedToken.Issuer);
        Assert.Contains(Audience, ((JwtSecurityToken)validatedToken).Audiences);
        Assert.NotEmpty(principal.Claims);
    }

    [Fact]
    public void GenerateToken_ValidOptions_UsesHs256Algorithm()
    {
        var response = CreateService(CreateValidOptions()).GenerateToken(CreateUser());
        var token = new JwtSecurityTokenHandler().ReadJwtToken(response.AccessToken);

        Assert.Equal(SecurityAlgorithms.HmacSha256, token.Header.Alg);
    }

    [Fact]
    public void GenerateToken_UserDetails_AddsExpectedIdentityClaims()
    {
        var user = CreateUser();
        var response = CreateService(CreateValidOptions()).GenerateToken(user);
        var principal = CreateTokenHandler().ValidateToken(
            response.AccessToken,
            CreateValidationParameters(CreateValidOptions()),
            out _);

        Assert.Equal(user.Id.ToString(), principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value);
        Assert.Equal(user.Email, principal.FindFirst(JwtRegisteredClaimNames.Email)?.Value);
        Assert.Equal(user.DisplayName, principal.FindFirst(JwtRegisteredClaimNames.Name)?.Value);

        var jti = principal.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
        Assert.True(Guid.TryParse(jti, out var jtiValue));
        Assert.NotEqual(Guid.Empty, jtiValue);
    }

    [Fact]
    public void GenerateToken_MultipleRoles_AddsAllRolesToValidatedPrincipal()
    {
        var user = CreateUser();
        user.Roles = new[] { "User", "Admin" };
        var options = CreateValidOptions();
        var response = CreateService(options).GenerateToken(user);
        var principal = CreateTokenHandler().ValidateToken(
            response.AccessToken,
            CreateValidationParameters(options),
            out _);

        Assert.Contains(principal.Claims, claim => claim.Type == "role" && claim.Value == "User");
        Assert.Contains(principal.Claims, claim => claim.Type == "role" && claim.Value == "Admin");
        Assert.True(principal.IsInRole("User"));
        Assert.True(principal.IsInRole("Admin"));
    }

    [Fact]
    public void GenerateToken_NullEmptyAndWhitespaceRoles_OmitsThoseRoleClaims()
    {
        var user = CreateUser();
        user.Roles = new string[] { "Admin", null!, string.Empty, "  " };
        var options = CreateValidOptions();
        var response = CreateService(options).GenerateToken(user);
        var principal = CreateTokenHandler().ValidateToken(
            response.AccessToken,
            CreateValidationParameters(options),
            out _);

        Assert.Equal(new[] { "Admin" }, principal.FindAll("role").Select(claim => claim.Value));
    }

    [Fact]
    public void GenerateToken_ValidOptions_SetsExpiresAtAndJwtExpiration()
    {
        var options = CreateValidOptions();
        var beforeGeneration = DateTimeOffset.UtcNow;
        var response = CreateService(options).GenerateToken(CreateUser());
        var afterGeneration = DateTimeOffset.UtcNow;
        var token = new JwtSecurityTokenHandler().ReadJwtToken(response.AccessToken);
        var jwtExpiration = new DateTimeOffset(token.ValidTo, TimeSpan.Zero);

        Assert.InRange(
            response.ExpiresAt,
            beforeGeneration.AddMinutes(ExpiryMinutes),
            afterGeneration.AddMinutes(ExpiryMinutes));
        Assert.True(Math.Abs((response.ExpiresAt - jwtExpiration).TotalSeconds) < 1);
    }

    [Fact]
    public void GenerateToken_ValidOptions_ReturnsBearerTokenType()
    {
        var response = CreateService(CreateValidOptions()).GenerateToken(CreateUser());

        Assert.Equal("Bearer", response.TokenType);
    }

    private static JwtServiceImplementation CreateService(JwtOptions options)
    {
        return new JwtServiceImplementation(MicrosoftOptions.Create(options));
    }

    private static JwtOptions CreateValidOptions()
    {
        return new JwtOptions
        {
            Issuer = Issuer,
            Audience = Audience,
            SigningKey = Convert.ToBase64String(CreateSigningKeyBytes()),
            ExpiryMinutes = ExpiryMinutes
        };
    }

    private static byte[] CreateSigningKeyBytes()
    {
        return Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
    }

    private static UserInfoResponse CreateUser()
    {
        return new UserInfoResponse
        {
            Id = Guid.Parse("81345e6d-2c2d-4ed1-b11b-8cb72c35b977"),
            Email = "user@example.test",
            DisplayName = "Test User",
            Roles = new[] { "User" }
        };
    }

    private static JwtSecurityTokenHandler CreateTokenHandler()
    {
        return new JwtSecurityTokenHandler
        {
            MapInboundClaims = false
        };
    }

    private static TokenValidationParameters CreateValidationParameters(JwtOptions options)
    {
        return new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = options.Issuer,
            ValidateAudience = true,
            ValidAudience = options.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(options.GetSigningKeyBytes()),
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ClockSkew = TimeSpan.Zero,
            NameClaimType = "name",
            RoleClaimType = "role"
        };
    }
}
