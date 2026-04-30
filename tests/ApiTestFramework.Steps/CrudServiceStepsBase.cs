using Allure.NUnit.Attributes;
using ApiTestFramework.Core.Assertions;
using ApiTestFramework.Core.Client;
using ApiTestFramework.Core.DI;
using RestSharp;
using Serilog;
using System.Net;

namespace ApiTestFramework.Steps;

/// <summary>
/// Generic base for CRUD step classes. Eliminates the copy-paste boilerplate of
/// Create / Get / GetAll / Update / Delete for every microservice.
///
/// Usage — create a thin subclass:
/// <code>
/// public class ProductServiceSteps
///     : CrudServiceStepsBase&lt;Product, CreateProductRequest, UpdateProductRequest&gt;
/// {
///     public ProductServiceSteps()
///         : base("ProductService", "/api/products", "/api/products/{id}") { }
/// }
/// </code>
///
/// Override any method to add service-specific behaviour.
/// </summary>
/// <typeparam name="TEntity">Response entity type (e.g. <c>Product</c>).</typeparam>
/// <typeparam name="TCreate">Create request DTO.</typeparam>
/// <typeparam name="TUpdate">Update request DTO.</typeparam>
public abstract class CrudServiceStepsBase<TEntity, TCreate, TUpdate>
    where TEntity : class
    where TCreate : class
    where TUpdate : class
{
    protected readonly ApiClient Client;
    protected readonly ILogger Logger;
    private readonly string _basePath;
    private readonly string _byIdPath;

    protected CrudServiceStepsBase(string serviceName, string basePath, string byIdPath)
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
    public virtual async Task<RestResponse<TEntity>> GetAsync(int id)
        => await Client.SendAsync<TEntity>(
            RequestBuilder.Create()
                .WithMethod(Method.Get)
                .WithPath(_byIdPath)
                .WithPathSegment("id", id));

    /// <summary>Gets all entities and returns the raw response.</summary>
    [AllureStep("Get all entities")]
    public virtual async Task<RestResponse<List<TEntity>>> GetAllAsync()
        => await Client.SendAsync<List<TEntity>>(
            RequestFactory.Get(_basePath));

    /// <summary>Updates an entity and returns the raw response.</summary>
    [AllureStep("Update entity with Id: {id}")]
    public virtual async Task<RestResponse<TEntity>> UpdateAsync(int id, TUpdate updateRequest)
        => await Client.SendAsync<TEntity>(
            RequestBuilder.Create()
                .WithMethod(Method.Put)
                .WithPath(_byIdPath)
                .WithPathSegment("id", id)
                .WithBody(updateRequest));

    /// <summary>Deletes an entity and returns the raw response.</summary>
    [AllureStep("Delete entity with Id: {id}")]
    public virtual async Task<RestResponse> DeleteAsync(int id)
        => await Client.SendAsync(
            RequestBuilder.Create()
                .WithMethod(Method.Delete)
                .WithPath(_byIdPath)
                .WithPathSegment("id", id));

    // ── Negative-path helpers (raw response, no assertion) ───────────────────────

    /// <summary>Attempts create — returns raw response for negative-path assertions.</summary>
    [AllureStep("Attempt to create entity (raw response)")]
    public virtual async Task<RestResponse<TEntity>> TryCreateAsync(TCreate createRequest)
        => await Client.SendAsync<TEntity>(
            RequestFactory.Post(_basePath, createRequest));

    /// <summary>Attempts update — returns raw response for negative-path assertions.</summary>
    [AllureStep("Attempt to update entity with Id: {id} (raw response)")]
    public virtual async Task<RestResponse<TEntity>> TryUpdateAsync(int id, TUpdate updateRequest)
        => await Client.SendAsync<TEntity>(
            RequestBuilder.Create()
                .WithMethod(Method.Put)
                .WithPath(_byIdPath)
                .WithPathSegment("id", id)
                .WithBody(updateRequest));

    /// <summary>Attempts delete — returns raw response for negative-path assertions.</summary>
    [AllureStep("Attempt to delete entity with Id: {id} (raw response)")]
    public virtual async Task<RestResponse> TryDeleteAsync(int id)
        => await Client.SendAsync(
            RequestBuilder.Create()
                .WithMethod(Method.Delete)
                .WithPath(_byIdPath)
                .WithPathSegment("id", id));
}
