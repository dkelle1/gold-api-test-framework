namespace OrderService.Models;

// EF Core entities — an order is an aggregate: Order (header) + OrderItems (lines)
public class Order
{
    public int Id { get; set; }

    // Customer (flattened columns, exposed as nested CustomerInfo in responses)
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public string? CustomerStreet { get; set; }
    public string? CustomerCity { get; set; }
    public string? CustomerPostalCode { get; set; }
    public string? CustomerCountry { get; set; }

    // Shipping (flattened columns, exposed as nested ShippingInfo in responses)
    public ShippingMethod ShippingMethod { get; set; } = ShippingMethod.Standard;
    public string? ShippingStreet { get; set; }
    public string? ShippingCity { get; set; }
    public string? ShippingPostalCode { get; set; }
    public string? ShippingCountry { get; set; }
    public string? ShippingNotes { get; set; }

    public decimal TotalPrice { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public List<OrderItem> Items { get; set; } = new();
}

// No navigation back to Order — keeps JSON (Redis cache) cycle-free
public class OrderItem
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal LineTotal { get; set; }
}

public enum OrderStatus
{
    Pending,
    Confirmed,
    Shipped,
    Delivered,
    Cancelled
}

public enum ShippingMethod
{
    Standard,
    Express,
    Pickup
}

// ===========================================================================
// Request DTOs — deliberately nested: one request carries several DTO levels
// (Customer -> Address, Items[], Shipping -> Address)
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

    /// <summary>Falls back to the customer address when omitted.</summary>
    public AddressRequest? Address { get; set; }
    public string? Notes { get; set; }
}

public class UpdateOrderRequest
{
    public OrderStatus? Status { get; set; }
    public CustomerRequest? Customer { get; set; }
    public ShippingRequest? Shipping { get; set; }
}

// ===========================================================================
// Response DTOs — nested, mirroring the aggregate
// ===========================================================================

public class OrderResponse
{
    public int Id { get; set; }
    public CustomerInfo Customer { get; set; } = new();
    public List<OrderItemInfo> Items { get; set; } = new();
    public ShippingInfo Shipping { get; set; } = new();
    public decimal TotalPrice { get; set; }
    public OrderStatus Status { get; set; }
    public AuditInfo Audit { get; set; } = new();

    public static OrderResponse FromEntity(Order o) => new()
    {
        Id = o.Id,
        Customer = new CustomerInfo
        {
            Name = o.CustomerName,
            Email = o.CustomerEmail,
            Address = AddressInfo.FromColumns(o.CustomerStreet, o.CustomerCity, o.CustomerPostalCode, o.CustomerCountry)
        },
        Items = o.Items.Select(i => new OrderItemInfo
        {
            ProductId = i.ProductId,
            ProductName = i.ProductName,
            UnitPrice = i.UnitPrice,
            Quantity = i.Quantity,
            LineTotal = i.LineTotal
        }).ToList(),
        Shipping = new ShippingInfo
        {
            Method = o.ShippingMethod,
            Address = AddressInfo.FromColumns(o.ShippingStreet, o.ShippingCity, o.ShippingPostalCode, o.ShippingCountry),
            Notes = o.ShippingNotes
        },
        TotalPrice = o.TotalPrice,
        Status = o.Status,
        Audit = new AuditInfo { CreatedAt = o.CreatedAt, UpdatedAt = o.UpdatedAt }
    };
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

    public static AddressInfo? FromColumns(string? street, string? city, string? postalCode, string? country)
    {
        if (street is null && city is null && postalCode is null && country is null) return null;
        return new AddressInfo
        {
            Street = street ?? string.Empty,
            City = city ?? string.Empty,
            PostalCode = postalCode ?? string.Empty,
            Country = country ?? string.Empty
        };
    }
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
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
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
