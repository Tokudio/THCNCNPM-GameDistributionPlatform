using GameDistributionPlatform.Application.DTOs;

namespace GameDistributionPlatform.Application.Interfaces;

public interface IJwtService
{
    AuthResponse GenerateToken(UserInfoResponse user);
}
