namespace GameDistributionPlatform.Application.Interfaces;

public interface IGamePackageStorage
{
    GamePackage? Open(Guid gameId);
}

public sealed record GamePackage(string FileName, Stream Content);
