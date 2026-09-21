using SmartWare.Domain.Enums;
using SmartWare.Domain.Services;

namespace SmartWare.UnitTests.Domain;

public sealed class OrderStatusTransitionTests
{
    [Theory]
    [InlineData(OrderStatus.Pending, OrderStatus.Processing)]
    [InlineData(OrderStatus.Pending, OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Shipping, OrderStatus.Completed)]
    [InlineData(OrderStatus.Shipping, OrderStatus.DeliveryFailed)]
    [InlineData(OrderStatus.Completed, OrderStatus.Returned)]
    [InlineData(OrderStatus.DeliveryFailed, OrderStatus.Shipping)]
    public void AllowedTransition_ReturnsTrue(OrderStatus current, OrderStatus target)
    {
        Assert.True(OrderStatusTransition.CanTransition(current, target));
    }

    [Theory]
    [InlineData(OrderStatus.Pending, OrderStatus.Completed)]
    [InlineData(OrderStatus.Processing, OrderStatus.Shipping)]
    [InlineData(OrderStatus.Cancelled, OrderStatus.Processing)]
    [InlineData(OrderStatus.Returned, OrderStatus.Completed)]
    [InlineData(OrderStatus.Completed, OrderStatus.Cancelled)]
    public void InvalidTransition_ReturnsFalse(OrderStatus current, OrderStatus target)
    {
        Assert.False(OrderStatusTransition.CanTransition(current, target));
    }
}
