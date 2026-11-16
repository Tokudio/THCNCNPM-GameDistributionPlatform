using Microsoft.Extensions.Options;

namespace GameDistributionPlatform.Infrastructure.Options;

public sealed class JwtOptionsValidator : IValidateOptions<JwtOptions>
{
    public ValidateOptionsResult Validate(string? name, JwtOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Issuer))
        {
            failures.Add("Configuration property 'Jwt:Issuer' is required and cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(options.Audience))
        {
            failures.Add("Configuration property 'Jwt:Audience' is required and cannot be empty.");
        }

        if (options.ExpiryMinutes <= 0)
        {
            failures.Add("Configuration property 'Jwt:ExpiryMinutes' must be greater than 0.");
        }

        if (string.IsNullOrWhiteSpace(options.SigningKey))
        {
            failures.Add("Configuration property 'Jwt:SigningKey' is required and cannot be empty.");
        }
        else
        {
            try
            {
                var keyBytes = Convert.FromBase64String(options.SigningKey);
                if (keyBytes.Length < 32)
                {
                    failures.Add($"Configuration property 'Jwt:SigningKey' is insecure: requires at least 32 bytes (256 bits), but decoded length was {keyBytes.Length} bytes.");
                }
            }
            catch (FormatException)
            {
                failures.Add("Configuration property 'Jwt:SigningKey' is invalid: must be a valid Base64-encoded string.");
            }
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}

