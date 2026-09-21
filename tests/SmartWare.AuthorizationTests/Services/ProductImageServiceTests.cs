using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using SmartWare.Web.Services.Products;

namespace SmartWare.AuthorizationTests.Services;

public sealed class ProductImageServiceTests
{
    [Fact]
    public async Task SaveAsync_ValidPng_SavesWithGeneratedName()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var service = CreateService(root);
            await using var content = new MemoryStream(
                [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0]);
            var image = new FormFile(content, 0, content.Length, "ImageFile", "sample.png")
            {
                Headers = new HeaderDictionary(),
                ContentType = "image/png"
            };

            var result = await service.SaveAsync(image);

            Assert.True(result.Succeeded);
            Assert.StartsWith("/uploads/products/", result.ImageUrl);
            Assert.EndsWith(".png", result.ImageUrl);
            var fileName = Path.GetFileName(result.ImageUrl!);
            Assert.True(File.Exists(Path.Combine(root, "wwwroot", "uploads", "products", fileName)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SaveAsync_SpoofedImage_IsRejected()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var service = CreateService(root);
            await using var content = new MemoryStream("not an image"u8.ToArray());
            var image = new FormFile(content, 0, content.Length, "ImageFile", "fake.png")
            {
                Headers = new HeaderDictionary(),
                ContentType = "image/png"
            };

            var result = await service.SaveAsync(image);

            Assert.False(result.Succeeded);
            Assert.Null(result.ImageUrl);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DeleteManagedImageAsync_DeletesOnlyManagedFile()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var uploadDirectory = Path.Combine(root, "wwwroot", "uploads", "products");
            Directory.CreateDirectory(uploadDirectory);
            var managedFile = Path.Combine(uploadDirectory, "managed.png");
            await File.WriteAllBytesAsync(managedFile, [1, 2, 3]);
            var unrelatedFile = Path.Combine(root, "unrelated.png");
            await File.WriteAllBytesAsync(unrelatedFile, [1, 2, 3]);
            var service = CreateService(root);

            await service.DeleteManagedImageAsync("/uploads/products/managed.png");
            await service.DeleteManagedImageAsync("/uploads/products/../../unrelated.png");

            Assert.False(File.Exists(managedFile));
            Assert.True(File.Exists(unrelatedFile));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static ProductImageService CreateService(string root) =>
        new(
            new TestWebHostEnvironment
            {
                ContentRootPath = root,
                WebRootPath = Path.Combine(root, "wwwroot")
            },
            NullLogger<ProductImageService>.Instance);

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"smartware-image-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "SmartWare.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
