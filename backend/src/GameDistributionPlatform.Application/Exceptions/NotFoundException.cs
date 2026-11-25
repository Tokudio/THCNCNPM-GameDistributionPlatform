namespace GameDistributionPlatform.Application.Exceptions;

public sealed class NotFoundException(string message) : Exception(message)
{
    public string? Code { get; init; }
}
