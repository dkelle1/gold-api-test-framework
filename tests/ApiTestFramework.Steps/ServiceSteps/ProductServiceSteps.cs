using System.Net;
using Allure.NUnit.Attributes;
using ApiTestFramework.Clients.ProductService;
using ApiTestFramework.Core.Client;
using ApiTestFramework.Core.Constants;
using ApiTestFramework.Steps.DataGenerators;
using RestSharp;

namespace ApiTestFramework.Steps.ServiceSteps;

/// <summary>
/// Step class for ProductService operations.
/// Inherits generic CRUD from <see cref="CrudServiceStepsBase{TEntity,TCreate,TUpdate}"/>.
/// Only service-specific behaviour (random-data overloads) lives here.
/// </summary>
public class ProductServiceSteps
    : CrudServiceStepsBase<Product, CreateProductRequest, UpdateProductRequest>
{
    public ProductServiceSteps()
        : base("ProductService", ProductServiceRoutes.Base, ProductServiceRoutes.ById) { }

    // ── Convenience overloads using random data ──────────────────────────────────

    /// <summary>Creates a product with random data and returns it (asserts 201).</summary>
    [AllureStep("Create a product with random data")]
    public Task<Product> CreateProductAsync()
        => CreateAsync(ProductDataGenerator.GenerateCreateProductRequest());

    /// <summary>Creates a product from a specific request and returns it (asserts 201).</summary>
    [AllureStep("Create a product")]
    public Task<Product> CreateProductAsync(CreateProductRequest request)
        => CreateAsync(request);

    /// <summary>Gets a product by id — returns raw response (caller asserts status).</summary>
    [AllureStep("Get product by Id: {id}")]
    public Task<RestResponse<Product>> GetProductAsync(int id)
        => GetAsync(id);

    /// <summary>Gets all products — returns raw response.</summary>
    [AllureStep("Get all products")]
    public Task<RestResponse<List<Product>>> GetAllProductsAsync()
        => GetAllAsync();

    /// <summary>Updates a product — returns raw response.</summary>
    [AllureStep("Update product with Id: {id}")]
    public Task<RestResponse<Product>> UpdateProductAsync(int id, UpdateProductRequest request)
        => UpdateAsync(id, request);

    /// <summary>Deletes a product — returns raw response.</summary>
    [AllureStep("Delete product with Id: {id}")]
    public Task<RestResponse> DeleteProductAsync(int id)
        => DeleteAsync(id);

    // ── Negative-path helpers ────────────────────────────────────────────────────

    [AllureStep("Attempt to create a product (raw response)")]
    public Task<RestResponse<Product>> TryCreateProductAsync(CreateProductRequest request)
        => TryCreateAsync(request);

    [AllureStep("Attempt to update product with Id: {id} (raw response)")]
    public Task<RestResponse<Product>> TryUpdateProductAsync(int id, UpdateProductRequest request)
        => TryUpdateAsync(id, request);

    [AllureStep("Attempt to delete product with Id: {id} (raw response)")]
    public Task<RestResponse> TryDeleteProductAsync(int id)
        => TryDeleteAsync(id);
}

