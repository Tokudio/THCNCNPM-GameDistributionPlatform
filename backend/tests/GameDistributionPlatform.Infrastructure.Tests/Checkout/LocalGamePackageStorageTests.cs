using GameDistributionPlatform.Infrastructure.Storage;
using Xunit;

namespace GameDistributionPlatform.Infrastructure.Tests.Checkout;

public sealed class LocalGamePackageStorageTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DoesNotOpenCorruptOrOversizedPackage(bool oversized)
    {
        var root = Directory.CreateTempSubdirectory("gdp-demo-test-");
        var packageDirectory = root.CreateSubdirectory("DemoPackages");
        const string name = "demo.zip";
        var file = Path.Combine(packageDirectory.FullName, name);
        try
        {
            using (var stream = File.Create(file))
            {
                if (oversized) stream.SetLength(8 * 1024 * 1024 + 1);
                else stream.Write("Dữ liệu fixture không đúng định dạng ZIP"u8);
            }

            var id = Guid.NewGuid();
            var storage = new LocalGamePackageStorage(root.FullName, new Dictionary<string, string> { [id.ToString()] = name });
            Assert.Null(storage.Open(id));
        }
        finally
        {
            File.Delete(file);
            packageDirectory.Delete();
            root.Delete();
        }
    }
}
