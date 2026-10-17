# Chapter 5 — Scaffolding and promoting tests

The contract already says what a caller can get back from every operation. The test scaffolder
turns that into a checklist of tests — one per documented response code — that you promote to
real tests where it matters. This chapter promotes the scaffolds of
`GET /api/products/category/{category}`, an endpoint the demo shop originally had no test for.

In this chapter:

- [Generating scaffolds](#generating-scaffolds)
- [Promoting a scaffold to a real test](#promoting-a-scaffold-to-a-real-test)
- [Making the test independent of other data](#making-the-test-independent-of-other-data)

---

## Generating scaffolds

### How to do it…

1. Regenerate (the same command as for builders):

   ```powershell
   ./examples/demo-shop/api-tests/scripts/regenerate-all.ps1
   ```

2. Open `DemoShop.Tests/Generated/ProductServiceGeneratedTests.cs.txt`. For the category endpoint
   the contract documents `200` and `401`, so there are two scaffolds:

   ```csharp
   [Test]
   [Description("GET /api/products/category/{category} documented success flow — returns 200")]
   [Category("GET")]
   [Category("Success")]
   public async Task GetProductsByCategory_ReturnsOk()
   {
       // Arrange : create or seed the entity required by the route, then request it with valid identifiers and authorization.
       // Act     : invoke the endpoint through a step class or ApiClient.
       // Assert  : response.ShouldHaveStatusCode((HttpStatusCode)200); then verify payload shape and key fields against swagger schema `List<Product>`.
       await Task.CompletedTask;
       Assert.Inconclusive("Scaffold only — implement test body.");
   }

   [Test]
   [Description("GET /api/products/category/{category} without token — returns 401")]
   [Category("Security")]
   public async Task GetProductsByCategory_WithoutToken_ReturnsUnauthorized()
   {
       // Arrange : send the request anonymously via RequestBuilder.WithoutAuth() (or an invalid token via WithBearerToken).
       ...
   }
   ```

### How it works…

`TestCaseScaffolder` maps each documented code to a scenario: success, `400` invalid payload,
`401` without token, `403` insufficient permissions, `404` unknown resource, `409` conflict… with
hints specific to the HTTP method. The file ends in `.cs.txt` so it is not compiled: it is a
checklist that is regenerated freely, never edited.

### There's more…

The scaffold is only as good as the contract. If the API can return `400` for a bad category but
the contract does not say so, there is no scaffold for it — fix the contract first
([chapter 2](02-onboard-a-service.md)).

---

## Promoting a scaffold to a real test

### Getting ready

There is a step for the endpoint ([chapter 4](04-write-steps.md)):
`ProductServiceSteps.GetProductsByCategoryAsync(category)`.

### How to do it…

1. Create a real fixture — here
   [`DemoShop.Tests/ProductService/ProductCategoryTests.cs`](../../examples/demo-shop/api-tests/DemoShop.Tests/ProductService/ProductCategoryTests.cs) —
   inheriting `BaseTest`, with Allure metadata:

   ```csharp
   [TestFixture]
   [AllureSuite("ProductService")]
   [AllureFeature("Browse by category")]
   public class ProductCategoryTests : BaseTest
   {
       private ProductServiceSteps _products = null!;
       private ProductServiceSteps _admin = null!;

       protected override void OnFixtureSetUp()
       {
           _products = new ProductServiceSteps();
           _admin = _products.AsIdentity(TestIdentities.Admin); // DELETE is admin-only
       }
   ```

2. Copy each scaffold you want, keep its name, and replace the hints with Arrange / Act / Assert.
   The success scaffold becomes a test of the endpoint's actual rule — *only active products of
   exactly that category*:

   ```csharp
   [Test]
   public async Task GetProductsByCategory_ReturnsOnlyActiveProductsOfThatCategory()
   {
       // Arrange
       var category = $"cat-{Guid.NewGuid():N}"[..20];
       var active = await CreateProductAsync(category);
       var deactivated = await CreateProductAsync(category);
       var otherCategory = await CreateProductAsync($"other-{Guid.NewGuid():N}"[..20]);
       (await _products.UpdateProductAsync(deactivated.Id, new UpdateProductRequest { IsActive = false }))
           .ShouldHaveStatusCode(HttpStatusCode.OK);

       // Act
       var response = await _products.GetProductsByCategoryAsync(category);

       // Assert
       response.ShouldHaveStatusCode(HttpStatusCode.OK);
       var products = response.ShouldHaveData();
       products.Select(p => p.Id).Should().Equal(active.Id);
       products.Should().OnlyContain(p => p.Category == category && p.IsActive);
   }
   ```

3. The security scaffold needs no step — anonymous requests are a builder option:

   ```csharp
   [Test]
   public async Task GetProductsByCategory_WithoutToken_ReturnsUnauthorized()
   {
       var client = ContainerProvider.ResolveNamed<ApiClient>("ProductService");

       var response = await client.SendAsync(
           RequestBuilder.Create()
               .WithMethod(Method.Get)
               .WithPath(ProductServiceRoutes.ByCategory)
               .WithPathSegment("category", "anything")
               .WithoutAuth());

       response.ShouldHaveStatusCode(HttpStatusCode.Unauthorized);
   }
   ```

4. Add the cases the contract cannot know about. An unknown category is **not** an error here:

   ```csharp
   [Test]
   public async Task GetProductsByCategory_WithUnknownCategory_ReturnsEmptyList()
   {
       var response = await _products.GetProductsByCategoryAsync($"none-{Guid.NewGuid():N}");

       response.ShouldHaveStatusCode(HttpStatusCode.OK);
       response.ShouldHaveData().Should().BeEmpty();
   }
   ```

5. Run them: `dotnet test examples/demo-shop/api-tests/DemoShop.Tests --filter "FullyQualifiedName~ProductCategoryTests"`.

### How it works…

The scaffold gave the *list* and the *names*; the test supplies the *meaning*. Steps keep each test
at the level of the business rule — no URLs, headers or JSON in sight.

---

## Making the test independent of other data

### How to do it…

1. Generate values no other test uses (`cat-<guid>`), so assertions can be exact (`Equal(active.Id)`)
   even when the database is shared and other tests run before or after.
2. Register cleanup immediately after creating something:

   ```csharp
   private async Task<Product> CreateProductAsync(string category)
   {
       var product = await _products.CreateProductAsync(
           ProductDataGenerator.GenerateCreateProductRequest(r => r.Category = category));
       RegisterCleanup(() => _admin.DeleteProductAsync(product.Id));
       return product;
   }
   ```

### How it works…

`RegisterCleanup` actions run in `TearDown` whatever the outcome, newest first; a failing cleanup
is logged and does not hide the test result. Deleting is admin-only in the demo shop, so cleanup
runs through the admin-bound steps.

### There's more…

Prefer assertions that hold whatever else is in the database ("contains mine", "only matching
items") over counts of global collections. That is what makes suites safe to run against shared
environments — and in parallel later.

## See also

- [Generators — test scaffolds](../framework/generators.md#test-scaffolds)
- [Core — `BaseTest` lifecycle](../framework/core.md#test-lifecycle--basetest)
