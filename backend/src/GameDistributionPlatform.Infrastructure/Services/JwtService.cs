using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using GameDistributionPlatform.Application.DTOs;
using GameDistributionPlatform.Application.Interfaces;
using GameDistributionPlatform.Infrastructure.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace GameDistributionPlatform.Infrastructure.Services;

public sealed class JwtService : IJwtService
{
    private readonly JwtOptions _jwtOptions;
    private readonly byte[] _signingKeyBytes;

    public JwtService(IOptions<JwtOptions> jwtOptions)
    {
        _jwtOptions = jwtOptions.Value;
        _jwtOptions.Validate();
        _signingKeyBytes = _jwtOptions.GetSigningKeyBytes();
    }

    public AuthResponse GenerateToken(UserInfoResponse user)
    {
        var expiresAtUtc = DateTime.UtcNow.AddMinutes(_jwtOptions.ExpiryMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Name, user.DisplayName),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        // Gán Role claim với Type là "role"
        foreach (var role in user.Roles)
        {
            if (!string.IsNullOrWhiteSpace(role))
            {
                claims.Add(new Claim("role", role));
            }
        }

        var securityKey = new SymmetricSecurityKey(_signingKeyBytes);
        var signingCredentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = _jwtOptions.Issuer,
            Audience = _jwtOptions.Audience,
            Expires = expiresAtUtc,
            SigningCredentials = signingCredentials
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var securityToken = tokenHandler.CreateToken(tokenDescriptor);
        var accessToken = tokenHandler.WriteToken(securityToken);

        return new AuthResponse
        {
            AccessToken = accessToken,
            TokenType = "Bearer",
            ExpiresAt = new DateTimeOffset(expiresAtUtc, TimeSpan.Zero)
        };
    }
}
