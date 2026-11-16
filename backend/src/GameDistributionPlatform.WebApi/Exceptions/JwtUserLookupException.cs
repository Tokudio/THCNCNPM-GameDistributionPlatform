namespace GameDistributionPlatform.WebApi.Exceptions;

public sealed class JwtUserLookupException : Exception
{
    public JwtUserLookupException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

