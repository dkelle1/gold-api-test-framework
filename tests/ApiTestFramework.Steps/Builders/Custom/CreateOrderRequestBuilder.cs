using ApiTestFramework.Clients.OrderService;

namespace ApiTestFramework.Steps.Builders.OrderService;

// ============================================================================
// Hand-written half of the generated CreateOrderRequestBuilder (partial).
// This file is NEVER touched by the generator — domain knowledge that cannot
// be derived from the OpenAPI spec lives here and survives regeneration.
// ============================================================================
public partial class CreateOrderRequestBuilder
{
    /// <summary>
    /// Enriches generated defaults: a realistic order exercises the full
    /// nesting — customer WITH address and explicit shipping info.
    /// </summary>
    partial void OnDefaultsApplied()
    {
        _instance.Customer = new CustomerRequestBuilder()
            .WithAddress(a => { }) // address builder defaults are enough
            .Build();

        _instance.Shipping = new ShippingRequestBuilder().Build();
    }

    /// <summary>
    /// Adds a single line for the given product. The one piece of data a
    /// builder can never invent is a valid ProductId — tests supply it here.
    /// </summary>
    public CreateOrderRequestBuilder ForProduct(int productId, int quantity = 1)
        => AddItem(i => i.WithProductId(productId).WithQuantity(quantity));

    /// <summary>
    /// Adds one line per (productId, quantity) pair — multi-item orders.
    /// </summary>
    public CreateOrderRequestBuilder ForProducts(params (int ProductId, int Quantity)[] lines)
    {
        foreach (var (productId, quantity) in lines)
            ForProduct(productId, quantity);
        return this;
    }
}
