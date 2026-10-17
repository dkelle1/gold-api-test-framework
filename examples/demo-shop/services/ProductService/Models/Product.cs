namespace ProductService.Models;

// EF Core entity
public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int StockQuantity { get; set; }
    public string Category { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}

// Nested response DTO
public class ProductResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public PriceInfo Price { get; set; } = new();
    public InventoryInfo Inventory { get; set; } = new();
    public string Category { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public AuditInfo Audit { get; set; } = new();

    public static ProductResponse FromEntity(Product p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        Description = p.Description,
        Price = new PriceInfo { Amount = p.Price, Currency = "USD" },
        Inventory = new InventoryInfo { StockQuantity = p.StockQuantity, InStock = p.StockQuantity > 0 },
        Category = p.Category,
        IsActive = p.IsActive,
        Audit = new AuditInfo { CreatedAt = p.CreatedAt, UpdatedAt = p.UpdatedAt }
    };
}

public class PriceInfo
{
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
}

public class InventoryInfo
{
    public int StockQuantity { get; set; }
    public bool InStock { get; set; }
}

public class AuditInfo
{
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

// Request DTOs (flat)
public class CreateProductRequest
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int StockQuantity { get; set; }
    public string Category { get; set; } = string.Empty;
}

public class UpdateProductRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public decimal? Price { get; set; }
    public int? StockQuantity { get; set; }
    public string? Category { get; set; }
    public bool? IsActive { get; set; }
}
