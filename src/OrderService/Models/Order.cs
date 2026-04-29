namespace OrderService.Models;

// EF Core entity
public class Order
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TotalPrice { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}

public enum OrderStatus
{
    Pending,
    Confirmed,
    Shipped,
    Delivered,
    Cancelled
}

// Nested response DTO
public class OrderResponse
{
    public int Id { get; set; }
    public ProductRef Product { get; set; } = new();
    public CustomerInfo Customer { get; set; } = new();
    public decimal TotalPrice { get; set; }
    public OrderStatus Status { get; set; }
    public AuditInfo Audit { get; set; } = new();

    public static OrderResponse FromEntity(Order o) => new()
    {
        Id = o.Id,
        Product = new ProductRef
        {
            ProductId = o.ProductId,
            ProductName = o.ProductName,
            UnitPrice = o.UnitPrice,
            Quantity = o.Quantity
        },
        Customer = new CustomerInfo
        {
            Name = o.CustomerName,
            Email = o.CustomerEmail
        },
        TotalPrice = o.TotalPrice,
        Status = o.Status,
        Audit = new AuditInfo { CreatedAt = o.CreatedAt, UpdatedAt = o.UpdatedAt }
    };
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
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

// Request DTOs (flat)
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

// DTO for cross-service product lookup (matches ProductService's nested response)
public class ProductDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public PriceDtoInfo Price { get; set; } = new();
    public InventoryDtoInfo Inventory { get; set; } = new();
    public bool IsActive { get; set; }
}

public class PriceDtoInfo
{
    public decimal Amount { get; set; }
}

public class InventoryDtoInfo
{
    public int StockQuantity { get; set; }
}
