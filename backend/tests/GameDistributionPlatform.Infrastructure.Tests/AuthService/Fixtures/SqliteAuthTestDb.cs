using GameDistributionPlatform.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GameDistributionPlatform.Infrastructure.Tests.AuthService.Fixtures;

/// <summary>
/// Quản lý SQLite In-Memory Database Connection độc lập cho từng test execution.
/// Giữ SqliteConnection mở trong suốt vòng đời của Fixture để bảo toàn schema và dữ liệu in-memory.
/// Cho phép Caller tạo nhiều AppDbContext trên cùng một connection.
/// Caller chịu trách nhiệm Dispose các instance AppDbContext do mình tạo thông qua using hoặc await using.
/// </summary>
public sealed class SqliteAuthTestDb : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private bool _disposed;

    public SqliteAuthTestDb()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        try
        {
            _connection.Open();

            _options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(_connection)
                .Options;

            using var initialContext = new AppDbContext(_options);
            initialContext.Database.EnsureCreated();
        }
        catch
        {
            _connection.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Tạo một instance AppDbContext mới trên cùng SQLite connection in-memory.
    /// Caller chịu trách nhiệm quản lý vòng đời và Dispose context này (thông qua using hoặc await using).
    /// </summary>
    public AppDbContext CreateContext()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return new AppDbContext(_options);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _connection.Close();
        _connection.Dispose();
    }
}
