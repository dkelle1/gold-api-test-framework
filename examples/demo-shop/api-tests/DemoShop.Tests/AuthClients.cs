namespace DemoShop.Tests;

/// <summary>
/// Names of the OAuth2 clients in Authentication:Clients of appsettings.test.json —
/// one per group of services behind the shared authorization server (R-CLIENT-3).
/// </summary>
public static class AuthClients
{
    /// <summary>ProductService group.</summary>
    public const string Catalog = "Catalog";

    /// <summary>OrderService group (may read the catalog).</summary>
    public const string Orders = "Orders";
}
