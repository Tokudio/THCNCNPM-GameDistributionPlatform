namespace GameDistributionPlatform.Application.DTOs;

public sealed class AuthResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string TokenType { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
}
