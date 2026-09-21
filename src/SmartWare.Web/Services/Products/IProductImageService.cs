using Microsoft.AspNetCore.Http;

namespace SmartWare.Web.Services.Products;

public interface IProductImageService
{
    Task<ProductImageSaveResult> SaveAsync(
        IFormFile image,
        CancellationToken cancellationToken = default);

    Task DeleteManagedImageAsync(
        string? imageUrl,
        CancellationToken cancellationToken = default);
}

public sealed record ProductImageSaveResult(
    bool Succeeded,
    string? ImageUrl,
    string? Error)
{
    public static ProductImageSaveResult Success(string imageUrl) =>
        new(true, imageUrl, null);

    public static ProductImageSaveResult Failure(string error) =>
        new(false, null, error);
}
