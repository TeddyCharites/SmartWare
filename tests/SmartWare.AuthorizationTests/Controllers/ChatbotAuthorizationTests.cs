using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using SmartWare.Web.Controllers;

namespace SmartWare.AuthorizationTests.Controllers;

public sealed class ChatbotAuthorizationTests
{
    [Fact]
    public void ChatbotController_RequiresAuthentication()
    {
        var attribute = Assert.Single(
            typeof(ChatbotController).GetCustomAttributes<AuthorizeAttribute>());

        Assert.Null(attribute.Policy);
        Assert.Null(attribute.Roles);
    }

    [Fact]
    public void ChatbotController_HasRateLimitPolicy()
    {
        var attribute = Assert.Single(
            typeof(ChatbotController).GetCustomAttributes<EnableRateLimitingAttribute>());

        Assert.Equal("chatbot", attribute.PolicyName);
    }

    [Fact]
    public void AskAction_ValidatesAntiForgeryToken()
    {
        var action = typeof(ChatbotController).GetMethod(nameof(ChatbotController.Ask));

        Assert.NotNull(action);
        Assert.Single(action.GetCustomAttributes<Microsoft.AspNetCore.Mvc.ValidateAntiForgeryTokenAttribute>());
    }

    [Fact]
    public void DeleteHistoryAction_ValidatesAntiForgeryToken()
    {
        var action = typeof(ChatbotController).GetMethod(nameof(ChatbotController.DeleteHistory));

        Assert.NotNull(action);
        Assert.Single(action.GetCustomAttributes<Microsoft.AspNetCore.Mvc.ValidateAntiForgeryTokenAttribute>());
    }

    [Fact]
    public void ConfirmDraftAction_RequiresCreateReceiptsPolicyAndAntiForgery()
    {
        var action = typeof(ChatbotController).GetMethod(nameof(ChatbotController.ConfirmDraft));

        Assert.NotNull(action);
        var authorize = Assert.Single(action.GetCustomAttributes<AuthorizeAttribute>());
        Assert.Equal(SmartWare.Domain.Constants.AuthorizationPolicies.CreateReceipts, authorize.Policy);
        Assert.Single(action.GetCustomAttributes<Microsoft.AspNetCore.Mvc.ValidateAntiForgeryTokenAttribute>());
    }

    [Theory]
    [InlineData(nameof(ChatbotController.History))]
    [InlineData(nameof(ChatbotController.HistoryDetails))]
    [InlineData(nameof(ChatbotController.DeleteHistory))]
    public void HistoryActions_AreExcludedFromAskRateLimit(string actionName)
    {
        var action = typeof(ChatbotController).GetMethod(actionName);

        Assert.NotNull(action);
        Assert.Single(action.GetCustomAttributes<DisableRateLimitingAttribute>());
    }
}
