// ==========================================================================
// PLACEHOLDER DTOs — These will be replaced by NSwag-generated code.
// Run generate-clients.bat to regenerate from swagger/order-swagger.json.
// Structure must match the nested request/response DTOs of OrderService:
//   CreateOrderRequest -> Customer (CustomerRequest -> AddressRequest),
//                         Items (List<OrderItemRequest>),
//                         Shipping (ShippingRequest -> AddressRequest)
//   Order (response)   -> Customer, Items[], Shipping, Audit
// ==========================================================================

namespace ApiTestFramework.Clients.OrderService;

public class Order
{
    public int Id { get; set; }
    public CustomerInfo Customer { get; set; } = new();
    public List<OrderItemInfo> Items { get; set; } = new();
    public ShippingInfo Shipping { get; set; } = new();
    public decimal TotalPrice { get; set; }
    public OrderStatus Status { get; set; }
    public AuditInfo Audit { get; set; } = new();
}

public class CustomerInfo
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public AddressInfo? Address { get; set; }
}

public class AddressInfo
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
}

public class OrderItemInfo
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal LineTotal { get; set; }
}

public class ShippingInfo
{
    public ShippingMethod Method { get; set; }
    public AddressInfo? Address { get; set; }
    public string? Notes { get; set; }
}

public class AuditInfo
{
    public System.DateTimeOffset CreatedAt { get; set; }
    public System.DateTimeOffset? UpdatedAt { get; set; }
}

public enum OrderStatus
{
    Pending = 0,
    Confirmed = 1,
    Shipped = 2,
    Delivered = 3,
    Cancelled = 4
}

public enum ShippingMethod
{
    Standard = 0,
    Express = 1,
    Pickup = 2
}

// ===========================================================================
// Request DTOs — nested: one request carries several DTO levels
// ===========================================================================

public class CreateOrderRequest
{
    public CustomerRequest Customer { get; set; } = new();
    public List<OrderItemRequest> Items { get; set; } = new();
    public ShippingRequest? Shipping { get; set; }
}

public class CustomerRequest
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public AddressRequest? Address { get; set; }
}

public class AddressRequest
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
}

public class OrderItemRequest
{
    public int ProductId { get; set; }
    public int Quantity { get; set; }
}

public class ShippingRequest
{
    public ShippingMethod Method { get; set; } = ShippingMethod.Standard;
    public AddressRequest? Address { get; set; }
    public string? Notes { get; set; }
}

public class UpdateOrderRequest
{
    public OrderStatus? Status { get; set; }
    public CustomerRequest? Customer { get; set; }
    public ShippingRequest? Shipping { get; set; }
}
