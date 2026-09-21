using System.ComponentModel.DataAnnotations;
using SmartWare.Web.ViewModels.Orders;

namespace SmartWare.AuthorizationTests.Validation;

public sealed class OrderFormValidationTests
{
    [Fact]
    public void DuplicateProducts_AreRejected()
    {
        var model = ValidModel();
        model.Lines.Add(new OrderLineViewModel { ProductId = 1, Quantity = 2, UnitPrice = 100_000 });

        Assert.Contains(Validate(model), error => error.MemberNames.Contains(nameof(model.Lines)));
    }

    [Fact]
    public void NonPositiveQuantity_IsRejected()
    {
        var model = ValidModel();
        model.Lines[0].Quantity = 0;

        Assert.Contains(Validate(model), error =>
            error.MemberNames.Contains(nameof(OrderLineViewModel.Quantity)));
    }

    [Fact]
    public void NegativeUnitPrice_IsRejected()
    {
        var model = ValidModel();
        model.Lines[0].UnitPrice = -1;

        Assert.Contains(Validate(model), error =>
            error.MemberNames.Contains(nameof(OrderLineViewModel.UnitPrice)));
    }

    [Fact]
    public void ValidOrder_PassesServerValidation()
    {
        Assert.Empty(Validate(ValidModel()));
    }

    private static List<ValidationResult> Validate(CreateOrderViewModel model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, true);
        foreach (var line in model.Lines)
        {
            Validator.TryValidateObject(line, new ValidationContext(line), results, true);
        }

        return results;
    }

    private static CreateOrderViewModel ValidModel() => new()
    {
        CustomerId = 1,
        OrderDate = DateTime.Now,
        Lines = [new OrderLineViewModel { ProductId = 1, Quantity = 3, UnitPrice = 100_000 }]
    };
}
