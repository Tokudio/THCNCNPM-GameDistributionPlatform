using System.Text;
using GameDistributionPlatform.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace GameDistributionPlatform.WebApi.Tests.Fixtures;

/// <summary>
/// Custom WebApplicationFactory khởi tạo môi trường Integration Test biệt lập cho WebApi.
/// Sử dụng SQLite In-Memory thay thế hoàn toàn SQL Server trước khi DatabaseSeeder chạy.
/// Quản lý vòng đời SqliteConnection và cấu hình JWT giả định độc lập.
/// </summary>
public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string TestJwtIssuer = "GameDistributionPlatformTestIssuer";
    public const string TestJwtAudience = "GameDistributionPlatformTestAudience";
    public static readonly string TestJwtSigningKey = Convert.ToBase64String(
        Encoding.UTF8.GetBytes("TestSigningKeyForIntegrationTestsOnly1234567890!"));

    private SqliteConnection? _sqliteConnection;
    protected virtual string TestEnvironment => "Testing";
    protected virtual bool IndependentConnections => false;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(TestEnvironment);

        // Cung cấp cấu hình Test qua UseSetting để nạp trước khi Program.cs validate
        builder.UseSetting("Jwt:Issuer", TestJwtIssuer);
        builder.UseSetting("Jwt:Audience", TestJwtAudience);
        builder.UseSetting("Jwt:SigningKey", TestJwtSigningKey);
        builder.UseSetting("Jwt:ExpiryMinutes", "60");
        builder.UseSetting("ConnectionStrings:DefaultConnection", "DataSource=:memory:");
        builder.UseSetting("BootstrapAdmin:Email", "");
        builder.UseSetting("BootstrapAdmin:Password", "");
        builder.UseSetting("BootstrapAdmin:DisplayName", "");

        builder.ConfigureServices(services =>
        {
            // 1. Loại bỏ các Service Registration liên quan đến SQL Server Provider cho AppDbContext
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.RemoveAll<AppDbContext>();

            // Loại bỏ các cấu hình nội bộ EF Core 9
            var efConfigs = services
                .Where(d => d.ServiceType.FullName?.Contains("IDbContextOptionsConfiguration") == true)
                .ToList();
            foreach (var config in efConfigs)
            {
                services.Remove(config);
            }

            // 2. Mở kết nối SQLite In-Memory và giữ mở trong suốt vòng đời Test Host
            var connectionString = IndependentConnections
                ? $"Data Source=checkout-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Pooling=False"
                : "DataSource=:memory:";
            _sqliteConnection = new SqliteConnection(connectionString);
            _sqliteConnection.Open();

            // 3. Khởi tạo schema SQLite bằng EnsureCreated() trước khi DatabaseSeeder chạy
            var sqliteOptions = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(_sqliteConnection)
                .Options;

            using (var initialContext = new AppDbContext(sqliteOptions))
            {
                // Fail-fast guard: Kiểm tra Database Provider thực tế phải là SQLite
                if (!initialContext.Database.IsSqlite())
                {
                    throw new InvalidOperationException("Fail-fast guard: Database Provider is NOT SQLite!");
                }

                initialContext.Database.EnsureCreated();
            }

            // 4. Đăng ký AppDbContext sử dụng SQLite Connection đã mở
            services.AddDbContext<AppDbContext>(options =>
            {
                if (IndependentConnections) options.UseSqlite(connectionString);
                else options.UseSqlite(_sqliteConnection);
            });
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = TestJwtIssuer,
                ["Jwt:Audience"] = TestJwtAudience,
                ["Jwt:SigningKey"] = TestJwtSigningKey,
                ["Jwt:ExpiryMinutes"] = "60",
                ["ConnectionStrings:DefaultConnection"] = "DataSource=:memory:",
                ["BootstrapAdmin:Email"] = "",
                ["BootstrapAdmin:Password"] = "",
                ["BootstrapAdmin:DisplayName"] = ""
            });
        });

        return base.CreateHost(builder);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _sqliteConnection?.Close();
            _sqliteConnection?.Dispose();
            _sqliteConnection = null;
        }
    }
}

