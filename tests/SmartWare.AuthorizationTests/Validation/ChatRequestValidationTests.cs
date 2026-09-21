using System.ComponentModel.DataAnnotations;
using SmartWare.Application.AI.Chatbot;

namespace SmartWare.AuthorizationTests.Validation;

public sealed class ChatRequestValidationTests
{
    [Theory]
    [InlineData("")]
    [InlineData("a")]
    public void EmptyOrTooShortMessage_IsRejected(string message)
    {
        Assert.NotEmpty(Validate(new ChatRequest { Message = message }));
    }

    [Fact]
    public void MessageLongerThanLimit_IsRejected()
    {
        Assert.NotEmpty(Validate(new ChatRequest { Message = new string('a', 1001) }));
    }

    [Fact]
    public void NormalQuestion_PassesValidation()
    {
        Assert.Empty(Validate(new ChatRequest { Message = "Sản phẩm nào sắp hết hàng?" }));
    }

    private static List<ValidationResult> Validate(ChatRequest request)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), results, true);
        return results;
    }
}
