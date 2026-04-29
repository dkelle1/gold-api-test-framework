// ==========================================================================
// PLACEHOLDER DTOs — These will be replaced by NSwag-generated code.
// Run generate-clients.bat to regenerate from swagger/product-swagger.json.
// Structure must match the nested ProductResponse returned by ProductService.
// ==========================================================================

namespace ApiTestFramework.Clients.ProductService;

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public PriceInfo Price { get; set; } = new();
    public InventoryInfo Inventory { get; set; } = new();
    public string Category { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public AuditInfo Audit { get; set; } = new();
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
    public System.DateTimeOffset CreatedAt { get; set; }
    public System.DateTimeOffset? UpdatedAt { get; set; }
}

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
