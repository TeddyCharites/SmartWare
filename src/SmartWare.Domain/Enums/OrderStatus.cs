namespace SmartWare.Domain.Enums;

public enum OrderStatus
{
    Pending = 1,
    Processing = 2,
    Shipping = 3,
    Completed = 4,
    Cancelled = 5,
    DeliveryFailed = 6,
    Returned = 7
}
