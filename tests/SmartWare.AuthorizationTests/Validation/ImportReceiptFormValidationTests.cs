using System.ComponentModel.DataAnnotations;
using SmartWare.Web.ViewModels.ImportReceipts;

namespace SmartWare.AuthorizationTests.Validation;

public sealed class ImportReceiptFormValidationTests
{
    [Fact]
    public void DuplicateProducts_AreRejected()
    {
        var model = ValidModel();
        model.Lines.Add(new ImportReceiptLineViewModel { ProductId = 1, Quantity = 2, UnitCost = 10 });

        var errors = Validate(model);

        Assert.Contains(errors, error => error.MemberNames.Contains(nameof(model.Lines)));
    }

    [Fact]
    public void InvalidLine_IsRejected()
    {
        var model = ValidModel();
        model.Lines[0].Quantity = 0;

        var errors = Validate(model);

        Assert.Contains(errors, error => error.MemberNames.Contains(nameof(ImportReceiptLineViewModel.Quantity)));
    }

    [Fact]
    public void ValidReceipt_PassesServerValidation()
    {
        Assert.Empty(Validate(ValidModel()));
    }

    private static List<ValidationResult> Validate(CreateImportReceiptViewModel model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, true);
        foreach (var line in model.Lines)
        {
            Validator.TryValidateObject(line, new ValidationContext(line), results, true);
        }

        return results;
    }

    private static CreateImportReceiptViewModel ValidModel() => new()
    {
        SupplierId = 1,
        WarehouseId = 1,
        Lines = [new ImportReceiptLineViewModel { ProductId = 1, Quantity = 5, UnitCost = 25_000 }]
    };
}
