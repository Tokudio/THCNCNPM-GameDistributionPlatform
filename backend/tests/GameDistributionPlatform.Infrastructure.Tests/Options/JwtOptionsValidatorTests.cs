using GameDistributionPlatform.Infrastructure.Options;
using Microsoft.Extensions.Options;
using Xunit;

namespace GameDistributionPlatform.Infrastructure.Tests.Options;

public sealed class JwtOptionsValidatorTests
{
    private readonly IValidateOptions<JwtOptions> _validator = new JwtOptionsValidator();

    [Fact]
    public void Validate_ValidOptionsWithThirtyTwoByteKey_ReturnsSuccess()
    {
        var result = _validator.Validate(null, CreateValidOptions());

        Assert.True(result.Succeeded);
        Assert.Empty(result.Failures ?? Array.Empty<string>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Validate_EmptyOrWhitespaceIssuer_ReturnsFailureForIssuer(string issuer)
    {
        var options = CreateValidOptions();
        options.Issuer = issuer;

        var result = _validator.Validate(null, options);

        AssertFailureContains(result, "Jwt:Issuer");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Validate_EmptyOrWhitespaceAudience_ReturnsFailureForAudience(string audience)
    {
        var options = CreateValidOptions();
        options.Audience = audience;

        var result = _validator.Validate(null, options);

        AssertFailureContains(result, "Jwt:Audience");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Validate_EmptyOrWhitespaceSigningKey_ReturnsFailureForSigningKey(string signingKey)
    {
        var options = CreateValidOptions();
        options.SigningKey = signingKey;

        var result = _validator.Validate(null, options);

        AssertFailureContains(result, "Jwt:SigningKey");
    }

    [Fact]
    public void Validate_InvalidBase64SigningKey_ReturnsFailureWithoutRevealingKey()
    {
        const string signingKey = "test-only-invalid-signing-key";
        var options = CreateValidOptions();
        options.SigningKey = signingKey;

        var result = _validator.Validate(null, options);

        AssertFailureContains(result, "Jwt:SigningKey");
        AssertFailureContains(result, "Base64");
        AssertDoesNotRevealSigningKey(result, signingKey);
    }

    [Fact]
    public void Validate_SigningKeyWithThirtyOneDecodedBytes_ReturnsFailureWithoutRevealingKey()
    {
        var signingKey = Convert.ToBase64String(CreateKeyBytes(31));
        var options = CreateValidOptions();
        options.SigningKey = signingKey;

        var result = _validator.Validate(null, options);

        AssertFailureContains(result, "Jwt:SigningKey");
        AssertFailureContains(result, "31 bytes");
        AssertDoesNotRevealSigningKey(result, signingKey);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_NonPositiveExpiryMinutes_ReturnsFailureForExpiry(int expiryMinutes)
    {
        var options = CreateValidOptions();
        options.ExpiryMinutes = expiryMinutes;

        var result = _validator.Validate(null, options);

        AssertFailureContains(result, "Jwt:ExpiryMinutes");
    }

    private static JwtOptions CreateValidOptions()
    {
        return new JwtOptions
        {
            Issuer = "https://issuer.test",
            Audience = "gamedistribution-tests",
            SigningKey = Convert.ToBase64String(CreateKeyBytes(32)),
            ExpiryMinutes = 60
        };
    }

    private static byte[] CreateKeyBytes(int length)
    {
        return Enumerable.Range(0, length).Select(value => (byte)value).ToArray();
    }

    private static void AssertFailureContains(ValidateOptionsResult result, string expectedText)
    {
        Assert.True(result.Failed);
        Assert.Contains(
            result.Failures ?? Array.Empty<string>(),
            failure => failure.Contains(expectedText, StringComparison.Ordinal));
    }

    private static void AssertDoesNotRevealSigningKey(ValidateOptionsResult result, string signingKey)
    {
        var failureMessages = string.Join(Environment.NewLine, result.Failures ?? Array.Empty<string>());
        Assert.DoesNotContain(signingKey, failureMessages, StringComparison.Ordinal);
    }
}
