namespace GameDistributionPlatform.Infrastructure.Options;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string SigningKey { get; set; } = string.Empty;
    public int ExpiryMinutes { get; set; } = 60;

    /// <summary>
    /// Kiểm tra tính hợp lệ của toàn bộ cấu hình JWT (Issuer, Audience, ExpiryMinutes, SigningKey).
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Issuer))
        {
            throw new InvalidOperationException("JWT configuration error: 'Jwt:Issuer' is required and cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(Audience))
        {
            throw new InvalidOperationException("JWT configuration error: 'Jwt:Audience' is required and cannot be empty.");
        }

        if (ExpiryMinutes <= 0)
        {
            throw new InvalidOperationException("JWT configuration error: 'Jwt:ExpiryMinutes' must be greater than 0.");
        }

        GetSigningKeyBytes();
    }

    /// <summary>
    /// Giải mã và trả về byte[] của SigningKey từ Base64.
    /// Phương thức này dùng chung cho cả JwtService và cấu hình JwtBearer trong WebApi.
    /// </summary>
    public byte[] GetSigningKeyBytes()
    {
        if (string.IsNullOrWhiteSpace(SigningKey))
        {
            throw new InvalidOperationException("JWT configuration error: 'Jwt:SigningKey' is required and cannot be empty.");
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(SigningKey);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("JWT configuration error: 'Jwt:SigningKey' must be a valid Base64-encoded string.", ex);
        }

        if (bytes.Length < 32)
        {
            throw new InvalidOperationException(
                $"JWT configuration error: 'Jwt:SigningKey' must represent at least 32 bytes (256 bits). Decoded length was {bytes.Length} bytes.");
        }

        return bytes;
    }
}
