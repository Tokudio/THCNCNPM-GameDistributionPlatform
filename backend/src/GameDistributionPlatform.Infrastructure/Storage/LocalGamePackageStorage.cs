using System.IO.Compression;
using GameDistributionPlatform.Application.Interfaces;

namespace GameDistributionPlatform.Infrastructure.Storage;

public sealed class LocalGamePackageStorage : IGamePackageStorage
{
    private const long MaxPackageBytes = 8 * 1024 * 1024;
    private readonly string _root;
    private readonly Dictionary<Guid, string> _packages = new();

    public LocalGamePackageStorage(string contentRoot, IReadOnlyDictionary<string, string> mappings)
    {
        _root = Path.Combine(contentRoot, "DemoPackages");
        foreach (var (key, name) in mappings)
        {
            if (!Guid.TryParseExact(key, "D", out var gameId) || gameId == Guid.Empty ||
                !IsPackageName(name) || !_packages.TryAdd(gameId, name))
                throw new InvalidOperationException("Cấu hình gói tải demo không hợp lệ.");
        }
    }

    public GamePackage? Open(Guid gameId)
    {
        if (!_packages.TryGetValue(gameId, out var name)) return null;

        var path = Path.Combine(_root, name);
        FileStream? stream = null;
        try
        {
            // Thư mục và tệp demo đều phải nằm trên storage cục bộ do server quản lý.
            if (IsLink(_root) || IsLink(path)) return null;
            stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 16_384, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length is <= 0 or > MaxPackageBytes) return null;

            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
            {
                if (zip.Entries.Count == 0) return null;
            }

            stream.Position = 0;
            var package = new GamePackage(name, stream);
            stream = null;
            return package;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return null;
        }
        finally
        {
            stream?.Dispose();
        }
    }

    private static bool IsPackageName(string name) =>
        name.Length is >= 5 and <= 120 && char.IsAsciiLetterOrDigit(name[0]) &&
        name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
        name.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_');

    private static bool IsLink(string path) => File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
}
