using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace GameDistributionPlatform.WebApi.Security;

internal static class AuthRateLimiting
{
    public const string Login = "auth-login";
    public const string Register = "auth-register";
    private const int BucketCount = 16_384;

    public static void AddAuthRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var login = ReadPolicy(configuration.GetSection("AuthRateLimiting:Login"), 5, 60);
        var register = ReadPolicy(configuration.GetSection("AuthRateLimiting:Register"), 3, 300);

        services.AddRateLimiter(options =>
        {
            options.AddPolicy(Login, context => ForRemoteAddress(context, login));
            options.AddPolicy(Register, context => ForRemoteAddress(context, register));
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                var response = context.HttpContext.Response;
                response.Headers.CacheControl = "no-store";
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var delay))
                {
                    response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(delay.TotalSeconds))
                        .ToString(CultureInfo.InvariantCulture);
                }

                var problem = new ProblemDetails
                {
                    Status = StatusCodes.Status429TooManyRequests,
                    Title = "Quá nhiều yêu cầu",
                    Detail = "Bạn đã gửi quá nhiều yêu cầu. Vui lòng thử lại sau.",
                    Instance = context.HttpContext.Request.Path
                };
                problem.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
                await response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json",
                    cancellationToken: cancellationToken);
            };
        });
    }

    private static RateLimitPartition<int> ForRemoteAddress(HttpContext context, (int Permits, int Seconds) policy)
    {
        var address = context.Connection.RemoteIpAddress;
        if (address?.IsIPv4MappedToIPv6 == true) address = address.MapToIPv4();

        // Chỉ dùng IP do server xác định. Tập bucket hữu hạn giới hạn bộ nhớ khi số IP tăng.
        var bucket = address is null ? -1 : (int)(unchecked((uint)HashCode.Combine(address)) % BucketCount);
        return RateLimitPartition.GetFixedWindowLimiter(bucket, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = policy.Permits,
            Window = TimeSpan.FromSeconds(policy.Seconds),
            QueueLimit = 0,
            AutoReplenishment = false
        });
    }

    private static (int Permits, int Seconds) ReadPolicy(IConfigurationSection section, int defaultPermits, int defaultSeconds)
    {
        var permits = section.GetValue("PermitLimit", defaultPermits);
        var seconds = section.GetValue("WindowSeconds", defaultSeconds);
        if (permits is < 1 or > 1_000 || seconds is < 1 or > 3_600)
            throw new InvalidOperationException($"Giới hạn trong {section.Path} không hợp lệ.");

        return (permits, seconds);
    }
}
