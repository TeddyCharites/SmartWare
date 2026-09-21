using SmartWare.Domain.Enums;

namespace SmartWare.Domain.Services;

public static class OrderStatusTransition
{
    public static bool CanTransition(OrderStatus current, OrderStatus target) =>
        (current, target) switch
        {
            (OrderStatus.Pending, OrderStatus.Processing or OrderStatus.Cancelled) => true,
            (OrderStatus.Processing, OrderStatus.Cancelled) => true,
            (OrderStatus.Shipping, OrderStatus.Completed or OrderStatus.DeliveryFailed or OrderStatus.Returned) => true,
            (OrderStatus.DeliveryFailed, OrderStatus.Shipping or OrderStatus.Returned) => true,
            (OrderStatus.Completed, OrderStatus.Returned) => true,
            _ => false
        };
}
