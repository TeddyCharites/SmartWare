using Microsoft.AspNetCore.Http;

namespace SmartWare.Web.Services.Products;

public sealed class ProductImageService(
    IWebHostEnvironment environment,
    ILogger<ProductImageService> logger) : IProductImageService
{
    public const long MaximumFileSize = 5 * 1024 * 1024;
    private const string PublicDirectory = "/uploads/products/";

    private static readonly IReadOnlyDictionary<string, string> ContentTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".jpg"] = "image/jpeg",
            [".png"] = "image/png",
            [".webp"] = "image/webp"
        };

    public async Task<ProductImageSaveResult> SaveAsync(
        IFormFile image,
        CancellationToken cancellationToken = default)
    {
        if (image.Length <= 0)
        {
            return ProductImageSaveResult.Failure("Tệp ảnh đang trống.");
        }

        if (image.Length > MaximumFileSize)
        {
            return ProductImageSaveResult.Failure("Ảnh sản phẩm không được vượt quá 5 MB.");
        }

        var detectedExtension = await DetectExtensionAsync(image, cancellationToken);
        if (detectedExtension is null)
        {
            return ProductImageSaveResult.Failure("Ảnh không hợp lệ. Chỉ chấp nhận tệp JPEG, PNG hoặc WebP.");
        }

        if (!ContentTypes.TryGetValue(detectedExtension, out var expectedContentType)
            || !string.Equals(image.ContentType, expectedContentType, StringComparison.OrdinalIgnoreCase))
        {
            return ProductImageSaveResult.Failure("Định dạng nội dung của ảnh không khớp với tệp đã chọn.");
        }

        var originalExtension = Path.GetExtension(image.FileName);
        var extensionMatches = detectedExtension == ".jpg"
            ? originalExtension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
              || originalExtension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            : originalExtension.Equals(detectedExtension, StringComparison.OrdinalIgnoreCase);

        if (!extensionMatches)
        {
            return ProductImageSaveResult.Failure("Phần mở rộng của ảnh không khớp với nội dung tệp.");
        }

        var uploadRoot = GetUploadRoot();
        Directory.CreateDirectory(uploadRoot);

        var fileName = $"{Guid.NewGuid():N}{detectedExtension}";
        var destination = Path.Combine(uploadRoot, fileName);

        try
        {
            await using var source = image.OpenReadStream();
            await using var target = new FileStream(
                destination,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous);
            await source.CopyToAsync(target, cancellationToken);
            return ProductImageSaveResult.Success($"{PublicDirectory}{fileName}");
        }
        catch (OperationCanceledException)
        {
            TryDelete(destination);
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogError(exception, "Không thể lưu ảnh sản phẩm {FileName}.", fileName);
            TryDelete(destination);
            return ProductImageSaveResult.Failure("Không thể lưu ảnh sản phẩm. Vui lòng thử lại.");
        }
    }

    public Task DeleteManagedImageAsync(
        string? imageUrl,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(imageUrl)
            || !imageUrl.StartsWith(PublicDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return Task.CompletedTask;
        }

        var fileName = imageUrl[PublicDirectory.Length..];
        if (string.IsNullOrWhiteSpace(fileName)
            || !string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        var uploadRoot = GetUploadRoot();
        var candidate = Path.GetFullPath(Path.Combine(uploadRoot, fileName));
        var expectedDirectory = Path.GetFullPath(uploadRoot)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        if (!candidate.StartsWith(expectedDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return Task.CompletedTask;
        }

        TryDelete(candidate);
        return Task.CompletedTask;
    }

    private static async Task<string?> DetectExtensionAsync(
        IFormFile image,
        CancellationToken cancellationToken)
    {
        var header = new byte[12];
        await using var stream = image.OpenReadStream();
        var offset = 0;
        while (offset < header.Length)
        {
            var count = await stream.ReadAsync(header.AsMemory(offset, header.Length - offset), cancellationToken);
            if (count == 0)
            {
                break;
            }

            offset += count;
        }

        if (offset >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            return ".jpg";
        }

        if (offset >= 8
            && header.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return ".png";
        }

        if (offset >= 12
            && header.AsSpan(0, 4).SequenceEqual("RIFF"u8)
            && header.AsSpan(8, 4).SequenceEqual("WEBP"u8))
        {
            return ".webp";
        }

        return null;
    }

    private string GetUploadRoot()
    {
        var webRoot = environment.WebRootPath;
        if (string.IsNullOrWhiteSpace(webRoot))
        {
            webRoot = Path.Combine(environment.ContentRootPath, "wwwroot");
        }

        return Path.Combine(webRoot, "uploads", "products");
    }

    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Không thể xóa ảnh sản phẩm tại {Path}.", path);
        }
    }
}
