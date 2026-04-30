using Allure.NUnit.Attributes;
using ApiTestFramework.Core.Assertions;
using ApiTestFramework.Core.Client;
using ApiTestFramework.Core.DI;
using RestSharp;
using Serilog;
using System.Net;

namespace ApiTestFramework.Steps;

/// <summary>
/// Generic CRUD step class for microservice test automation.
/// Can be used directly (zero boilerplate) or subclassed for service-specific behaviour.
///
/// Direct use — pure CRUD service, no subclass needed:
/// <code>
/// var steps = new CrudServiceSteps&lt;InventoryItem, CreateItemRequest, UpdateItemRequest&gt;(
///     "InventoryService", "/api/inventory", "/api/inventory/{id}");
/// </code>
///
/// Subclass only when a service has extra behaviour (custom routes, cross-service setup, etc.):
/// <code>
/// public class OrderServiceSteps
///     : CrudServiceSteps&lt;Order, CreateOrderRequest, UpdateOrderRequest&gt;
/// {
///     public OrderServiceSteps()
///         : base("OrderService", "/api/orders", "/api/orders/{id}") { }
///
///     public async Task&lt;Order&gt; CreateOrderWithProductAsync() { ... }
/// }
/// </code>
/// </summary>
/// <typeparam name="TEntity">Response entity type (e.g. <c>Product</c>).</typeparam>
/// <typeparam name="TCreate">Create request DTO.</typeparam>
/// <typeparam name="TUpdate">Update request DTO.</typeparam>
public class CrudServiceSteps<TEntity, TCreate, TUpdate>
    where TEntity : class
    where TCreate : class
    where TUpdate : class
{
    protected readonly ApiClient Client;
    protected readonly ILogger Logger;
    private readonly string _basePath;
    private readonly string _byIdPath;

    public CrudServiceSteps(string serviceName, string basePath, string byIdPath)
    {
        Client = ContainerProvider.ResolveNamed<ApiClient>(serviceName);
        Logger = Log.ForContext(GetType());
        _basePath = basePath;
        _byIdPath = byIdPath;
    }

    // ── Positive-path methods (assert success, return unwrapped entity) ──────────

    /// <summary>Creates an entity from the given request and asserts 201 Created.</summary>
    [AllureStep("Create entity")]
    public virtual async Task<TEntity> CreateAsync(TCreate createRequest)
    {
        var response = await Client.SendAsync<TEntity>(
            RequestFactory.Post(_basePath, createRequest));

        response.ShouldHaveStatusCode(HttpStatusCode.Created);
        return response.ShouldHaveData();
    }

    /// <summary>Gets an entity by id and returns the raw response (caller asserts status).</summary>
    [AllureStep("Get entity by Id: {id}")]
    public virtual Task<RestResponse<TEntity>> GetAsync(int id)
        => Client.SendAsync<TEntity>(
            RequestBuilder.Create()
                .WithMethod(Method.Get)
                .WithPath(_byIdPath)
                .WithPathSegment("id", id));

    /// <summary>Gets all entities and returns the raw response.</summary>
    [AllureStep("Get all entities")]
    public virtual Task<RestResponse<List<TEntity>>> GetAllAsync()
        => Client.SendAsync<List<TEntity>>(
            RequestFactory.Get(_basePath));

    /// <summary>Updates an entity and returns the raw response.</summary>
    [AllureStep("Update entity with Id: {id}")]
    public virtual Task<RestResponse<TEntity>> UpdateAsync(int id, TUpdate updateRequest)
        => Client.SendAsync<TEntity>(
            RequestBuilder.Create()
                .WithMethod(Method.Put)
                .WithPath(_byIdPath)
                .WithPathSegment("id", id)
                .WithBody(updateRequest));

    /// <summary>Deletes an entity and returns the raw response.</summary>
    [AllureStep("Delete entity with Id: {id}")]
    public virtual Task<RestResponse> DeleteAsync(int id)
        => Client.SendAsync(
            RequestBuilder.Create()
                .WithMethod(Method.Delete)
                .WithPath(_byIdPath)
                .WithPathSegment("id", id));

    // ── Negative-path helpers (raw response, no assertion) ───────────────────────

    /// <summary>Attempts create — returns raw response for negative-path assertions.</summary>
    [AllureStep("Attempt to create entity (raw response)")]
    public virtual Task<RestResponse<TEntity>> TryCreateAsync(TCreate createRequest)
        => Client.SendAsync<TEntity>(
            RequestFactory.Post(_basePath, createRequest));

    /// <summary>Attempts update — returns raw response for negative-path assertions.</summary>
    [AllureStep("Attempt to update entity with Id: {id} (raw response)")]
    public virtual Task<RestResponse<TEntity>> TryUpdateAsync(int id, TUpdate updateRequest)
        => Client.SendAsync<TEntity>(
            RequestBuilder.Create()
                .WithMethod(Method.Put)
                .WithPath(_byIdPath)
                .WithPathSegment("id", id)
                .WithBody(updateRequest));

    /// <summary>Attempts delete — returns raw response for negative-path assertions.</summary>
    [AllureStep("Attempt to delete entity with Id: {id} (raw response)")]
    public virtual Task<RestResponse> TryDeleteAsync(int id)
        => Client.SendAsync(
            RequestBuilder.Create()
                .WithMethod(Method.Delete)
                .WithPath(_byIdPath)
                .WithPathSegment("id", id));
}
