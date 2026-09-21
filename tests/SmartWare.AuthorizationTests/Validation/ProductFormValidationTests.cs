using System.ComponentModel.DataAnnotations;
using SmartWare.Web.ViewModels.Products;

namespace SmartWare.AuthorizationTests.Validation;

public sealed class ProductFormValidationTests
{
    [Fact]
    public void MaximumStock_CannotBeLowerThanMinimumStock()
    {
        var model = ValidModel();
        model.MinimumStock = 20;
        model.MaximumStock = 10;

        var errors = Validate(model);

        Assert.Contains(errors, error =>
            error.MemberNames.Contains(nameof(ProductFormViewModel.MaximumStock)));
    }

    [Fact]
    public void ValidProduct_PassesServerValidation()
    {
        var errors = Validate(ValidModel());

        Assert.Empty(errors);
    }

    private static List<ValidationResult> Validate(ProductFormViewModel model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, true);
        return results;
    }

    private static ProductFormViewModel ValidModel() => new()
    {
        Sku = "SP-001",
        Name = "Sản phẩm kiểm thử",
        CategoryId = 1,
        SupplierId = 1,
        UnitOfMeasure = "Cái",
        SellingPrice = 100_000,
        MinimumStock = 5,
        MaximumStock = 50,
        IsActive = true
    };
}
