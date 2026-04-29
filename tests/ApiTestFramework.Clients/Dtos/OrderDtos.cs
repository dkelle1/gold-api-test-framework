// ==========================================================================
// PLACEHOLDER DTOs — These will be replaced by NSwag-generated code.
// Run generate-clients.bat to regenerate from swagger/order-swagger.json.
// Structure must match the nested OrderResponse returned by OrderService.
// ==========================================================================

namespace ApiTestFramework.Clients.OrderService;

public class Order
{
    public int Id { get; set; }
    public ProductRef Product { get; set; } = new();
    public CustomerInfo Customer { get; set; } = new();
    public decimal TotalPrice { get; set; }
    public OrderStatus Status { get; set; }
    public AuditInfo Audit { get; set; } = new();
}

public class ProductRef
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
}

public class CustomerInfo
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
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

public class CreateOrderRequest
{
    public int ProductId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public int Quantity { get; set; }
}

public class UpdateOrderRequest
{
    public int? Quantity { get; set; }
    public OrderStatus? Status { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerEmail { get; set; }
}
