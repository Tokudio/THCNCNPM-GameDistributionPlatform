using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;

namespace GameDistributionPlatform.WebApi.Tests.Fixtures;

/// <summary>
/// Helper tiện ích cho Integration Tests:
/// - Tạo JWT Test Tokens (hợp lệ, hết hạn, sai issuer, sai chữ ký, v.v.)
/// - Đọc và deserialize ProblemDetails từ HTTP Response
/// </summary>
public static class IntegrationTestHelper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Tạo JWT token với các tham số tùy biến phục vụ kiểm thử JWT Middleware.
    /// </summary>
    public static string CreateTestJwtToken(
        Guid? userId = null,
        string? email = null,
        string? role = null,
        IEnumerable<string>? roles = null,
        string? issuer = CustomWebApplicationFactory.TestJwtIssuer,
        string? audience = CustomWebApplicationFactory.TestJwtAudience,
        string? signingKey = null,
        DateTime? expires = null,
        bool includeSub = true,
        bool includeExpiration = true,
        string? customSubValue = null)
    {
        var keyToUse = signingKey ?? CustomWebApplicationFactory.TestJwtSigningKey;
        var keyBytes = Convert.FromBase64String(keyToUse);
        var securityKey = new SymmetricSecurityKey(keyBytes);
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>();

        if (includeSub)
        {
            var subValue = customSubValue ?? (userId?.ToString() ?? Guid.NewGuid().ToString());
            claims.Add(new Claim(JwtRegisteredClaimNames.Sub, subValue));
        }

        if (!string.IsNullOrEmpty(email))
        {
            claims.Add(new Claim(JwtRegisteredClaimNames.Email, email));
        }

        if (!string.IsNullOrEmpty(role))
        {
            claims.Add(new Claim("role", role));
        }

        if (roles != null)
        {
            foreach (var r in roles)
            {
                if (!string.IsNullOrWhiteSpace(r))
                {
                    claims.Add(new Claim("role", r));
                }
            }
        }

        var expirationTime = expires ?? DateTime.UtcNow.AddMinutes(60);
        var notBeforeTime = expirationTime < DateTime.UtcNow
            ? expirationTime.AddMinutes(-5)
            : DateTime.UtcNow.AddMinutes(-1);

        if (!includeExpiration)
        {
            var noExpirationHandler = new JwtSecurityTokenHandler();
            var noExpirationToken = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: claims,
                notBefore: notBeforeTime,
                expires: null,
                signingCredentials: credentials);
            return noExpirationHandler.WriteToken(noExpirationToken);
        }

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            NotBefore = notBeforeTime,
            Expires = includeExpiration ? expirationTime : null,
            Issuer = issuer,
            Audience = audience,
            SigningCredentials = credentials
        };

        var handler = new JwtSecurityTokenHandler();
        var token = handler.CreateToken(tokenDescriptor);
        return handler.WriteToken(token);
    }

    /// <summary>
    /// Đọc ProblemDetails từ HttpResponseMessage.
    /// </summary>
    public static async Task<ProblemDetails?> ReadProblemDetailsAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        return JsonSerializer.Deserialize<ProblemDetails>(content, JsonOptions);
    }

    /// <summary>
    /// Đọc ValidationProblemDetails từ HttpResponseMessage.
    /// </summary>
    public static async Task<ValidationProblemDetails?> ReadValidationProblemDetailsAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        return JsonSerializer.Deserialize<ValidationProblemDetails>(content, JsonOptions);
    }
}
