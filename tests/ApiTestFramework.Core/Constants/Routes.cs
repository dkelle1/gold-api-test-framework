namespace ApiTestFramework.Core.Constants;

/// <summary>
/// API route constants for ProductService.
/// Can also be generated via NSwag — these serve as a fallback / manual reference.
/// </summary>
public static class ProductServiceRoutes
{
    public const string Base = "/api/products";
    public const string ById = "/api/products/{id}";
    public const string ByCategory = "/api/products/category/{category}";
}

/// <summary>
/// API route constants for OrderService.
/// </summary>
public static class OrderServiceRoutes
{
    public const string Base = "/api/orders";
    public const string ById = "/api/orders/{id}";
    public const string ByCustomerEmail = "/api/orders/customer/{email}";
}

/// <summary>
/// API route constants for AuthService.
/// </summary>
public static class AuthServiceRoutes
{
    public const string Register = "/api/auth/register";
    public const string Login = "/api/auth/login";
}

/// <summary>
/// API route constants for ImportService.
/// </summary>
public static class ImportServiceRoutes
{
    public const string ProductCsvImport = "/api/imports/products/csv";
    public const string StatusByBatchId = "/api/imports/{batchId}/status";
}
