namespace GameDistributionPlatform.Application.Exceptions;

public sealed class CheckoutUnavailableException()
    : Exception("Mock checkout is available only in Development and Testing.");

public sealed class GameAlreadyOwnedException()
    : ConflictException("You already own this game.");
