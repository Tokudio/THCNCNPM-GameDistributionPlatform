using GameDistributionPlatform.Infrastructure.Options;
using Xunit;

namespace GameDistributionPlatform.Infrastructure.Tests.Options;

public sealed class JwtOptionsTests
{
    [Fact]
    public void Validate_ValidOptionsWithThirtyTwoByteKey_DoesNotThrow()
    {
        var keyBytes = CreateKeyBytes(32);
        var options = CreateValidOptions(keyBytes);

        options.Validate();

        Assert.Equal(keyBytes, options.GetSigningKeyBytes());
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Validate_EmptyOrWhitespaceIssuer_ThrowsInvalidOperationException(string issuer)
    {
        var options = CreateValidOptions();
        options.Issuer = issuer;

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Validate_EmptyOrWhitespaceAudience_ThrowsInvalidOperationException(string audience)
    {
        var options = CreateValidOptions();
        options.Audience = audience;

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Validate_EmptyOrWhitespaceSigningKey_ThrowsInvalidOperationException(string signingKey)
    {
        var options = CreateValidOptions();
        options.SigningKey = signingKey;

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public void Validate_InvalidBase64SigningKey_ThrowsWithoutRevealingKey()
    {
        const string signingKey = "test-only-invalid-signing-key";
        var options = CreateValidOptions();
        options.SigningKey = signingKey;

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);

        Assert.IsType<FormatException>(exception.InnerException);
        Assert.DoesNotContain(signingKey, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_SigningKeyWithThirtyOneDecodedBytes_ThrowsWithoutRevealingKey()
    {
        var signingKey = Convert.ToBase64String(CreateKeyBytes(31));
        var options = CreateValidOptions();
        options.SigningKey = signingKey;

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);

        Assert.Contains("31 bytes", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(signingKey, exception.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_NonPositiveExpiryMinutes_ThrowsInvalidOperationException(int expiryMinutes)
    {
        var options = CreateValidOptions();
        options.ExpiryMinutes = expiryMinutes;

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    private static JwtOptions CreateValidOptions(byte[]? signingKeyBytes = null)
    {
        return new JwtOptions
        {
            Issuer = "https://issuer.test",
            Audience = "gamedistribution-tests",
            SigningKey = Convert.ToBase64String(signingKeyBytes ?? CreateKeyBytes(32)),
            ExpiryMinutes = 60
        };
    }

    private static byte[] CreateKeyBytes(int length)
    {
        return Enumerable.Range(0, length).Select(value => (byte)value).ToArray();
    }
}
