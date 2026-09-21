using System.ComponentModel.DataAnnotations;
using SmartWare.Web.ViewModels.Customers;

namespace SmartWare.AuthorizationTests.Validation;

public sealed class CustomerFormValidationTests
{
    [Theory]
    [InlineData("KH 001")]
    [InlineData("KH@001")]
    public void InvalidCustomerCode_IsRejected(string code)
    {
        var model = ValidModel();
        model.Code = code;

        Assert.Contains(Validate(model), error => error.MemberNames.Contains(nameof(model.Code)));
    }

    [Fact]
    public void InvalidEmail_IsRejected()
    {
        var model = ValidModel();
        model.Email = "not-an-email";

        Assert.Contains(Validate(model), error => error.MemberNames.Contains(nameof(model.Email)));
    }

    [Fact]
    public void ValidCustomer_PassesServerValidation()
    {
        Assert.Empty(Validate(ValidModel()));
    }

    private static List<ValidationResult> Validate(CustomerFormViewModel model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, true);
        return results;
    }

    private static CustomerFormViewModel ValidModel() => new()
    {
        Code = "KH-001",
        Name = "Khách hàng mẫu",
        Phone = "0901234567",
        Email = "khachhang@example.com",
        Address = "Thành phố Hồ Chí Minh",
        IsActive = true
    };
}
