using System.ComponentModel.DataAnnotations;
using SmartWare.Web.ViewModels.ExportReceipts;

namespace SmartWare.AuthorizationTests.Validation;

public sealed class ExportReceiptFormValidationTests
{
    [Fact]
    public void DuplicateProducts_AreRejected()
    {
        var model = ValidModel();
        model.Lines.Add(new ExportReceiptLineViewModel { ProductId = 1, Quantity = 2 });

        var errors = Validate(model);

        Assert.Contains(errors, error => error.MemberNames.Contains(nameof(model.Lines)));
    }

    [Fact]
    public void NonPositiveQuantity_IsRejected()
    {
        var model = ValidModel();
        model.Lines[0].Quantity = 0;

        var errors = Validate(model);

        Assert.Contains(errors, error =>
            error.MemberNames.Contains(nameof(ExportReceiptLineViewModel.Quantity)));
    }

    [Fact]
    public void ValidDirectExport_PassesServerValidation()
    {
        Assert.Empty(Validate(ValidModel()));
    }

    private static List<ValidationResult> Validate(CreateExportReceiptViewModel model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, true);
        foreach (var line in model.Lines)
        {
            Validator.TryValidateObject(line, new ValidationContext(line), results, true);
        }

        return results;
    }

    private static CreateExportReceiptViewModel ValidModel() => new()
    {
        WarehouseId = 1,
        Lines = [new ExportReceiptLineViewModel { ProductId = 1, Quantity = 5 }]
    };
}
