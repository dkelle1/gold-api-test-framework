# Chapter 6 — Authentication and identities

Real APIs are tested as *someone*: an anonymous visitor, a customer, an administrator, a partner
system. This chapter configures how requests are authenticated, provisions several users once
per suite, and tests an authorization rule — the same operation, different outcomes per role.

In this chapter:

- [Choosing an authentication mode](#choosing-an-authentication-mode)
- [Provisioning identities for the whole suite](#provisioning-identities-for-the-whole-suite)
- [Testing an authorization rule](#testing-an-authorization-rule)
- [Minting identities inside a test](#minting-identities-inside-a-test)

---

## Choosing an authentication mode

### How to do it…

Pick the mode matching your product in `appsettings.test.json`:

| Your API uses… | Mode | You write |
|----------------|------|-----------|
| a login endpoint returning a token (the demo shop) | `SessionToken` | the login call in `GlobalSetup` |
| OAuth2 client credentials (Keycloak, Auth0, Entra ID…) | `OAuth2ClientCredentials` | nothing — config + secret |
| an API key header | `ApiKey` | nothing |
| a fixed token from a vault | `StaticToken` | nothing |
| something else (HMAC, cookies) | `Custom` | one `IAuthenticationProvider` |

```json
"Authentication": { "Mode": "SessionToken" }
```

Secrets go into CI variables, e.g. `TEST_Authentication__OAuth2__ClientSecret`.

### How it works…

On every `Build()` the request builder asks the active provider for a header unless the test chose
otherwise (`WithoutAuth`, `AsIdentity`, an explicit header). Tests never touch tokens.

---

## Provisioning identities for the whole suite

### Getting ready

Decide which roles your tests need. The demo shop needs a regular user and an admin.

### How to do it…

1. List them in `appsettings.test.json`:

   ```json
   "Identities": [
     { "Name": "admin", "Role": "Admin" },
     { "Name": "user",  "Role": "User" }
   ]
   ```

2. In `GlobalSetup`, create a user per entry and store its token under the identity name
   (from [`DemoShop.Tests/GlobalSetup.cs`](../../examples/demo-shop/api-tests/DemoShop.Tests/GlobalSetup.cs)):

   ```csharp
   foreach (var identity in config.Identities)
   {
       var suffix = Guid.NewGuid().ToString("N")[..8];
       var token = await RegisterUserAndGetTokenAsync(authClient, new RegisterRequest
       {
           Username = $"test_{identity.Name}_{suffix}",
           Email = $"test_{identity.Name}_{suffix}@test.com",
           Password = "TestPass123!",
           Role = identity.Role
       });
       TokenProvider.SetToken(identity.Name, token);
   }
   ```

3. Give the names constants so typos do not compile
   ([`TestIdentities.cs`](../../examples/demo-shop/api-tests/DemoShop.Tests/TestIdentities.cs)):

   ```csharp
   public static class TestIdentities
   {
       public const string Admin = "admin";
       public const string User = "user";
   }
   ```

### How it works…

`TokenProvider` keeps one token per identity name next to the default one. How a user is *created*
is product-specific — the demo shop's register endpoint accepts a role; your product might log in
seeded accounts or ask an IdP — so that code lives in your `GlobalSetup`, not in the framework.

### There's more…

The demo's `GlobalSetup` falls back to **login** when a fixed username already exists (409), so the
suite can be re-run against the same database.

---

## Testing an authorization rule

In the demo shop, deleting a product requires the `Admin` role.

### How to do it…

Bind identities to the steps and compare outcomes
([`ProductAuthorizationTests`](../../examples/demo-shop/api-tests/DemoShop.Tests/ProductService/ProductAuthorizationTests.cs)):

```csharp
[Test]
public async Task DeleteProduct_UserThenAdmin_EnforcesRolePerRequest()
{
    var product = await _steps.CreateProductAsync();

    var asUser = await _steps.AsIdentity(TestIdentities.User).DeleteProductAsync(product.Id);
    asUser.ShouldHaveStatusCode(HttpStatusCode.Forbidden);

    // the product must survive the forbidden attempt
    (await _steps.GetProductAsync(product.Id)).ShouldHaveStatusCode(HttpStatusCode.OK);

    var asAdmin = await _steps.AsIdentity(TestIdentities.Admin).DeleteProductAsync(product.Id);
    asAdmin.ShouldHaveStatusCode(HttpStatusCode.NoContent);
}
```

And the anonymous case, without any identity:

```csharp
var response = await client.SendAsync(
    RequestBuilder.Create().WithMethod(Method.Delete)
        .WithPath(ProductServiceRoutes.ById).WithPathSegment("id", product.Id)
        .WithoutAuth());
response.ShouldHaveStatusCode(HttpStatusCode.Unauthorized);
```

### How it works…

`AsIdentity(name)` returns a *copy* of the step class whose every request carries that identity's
token; `_steps` itself keeps using the default identity. One test can therefore switch actors
request by request.

### There's more…

Always assert the side effect of a forbidden call ("the product still exists"), not only the
status code — a `403` returned *after* deleting is still a bug.

---

## Minting identities inside a test

### How to do it…

When a test needs its own user (a fresh customer, an account in a particular state), create it in
the test:

```csharp
// named: stored in TokenProvider, usable with AsIdentity(name)
var auth = await _authSteps.RegisterIdentityAsync($"customer-{Guid.NewGuid():N}", role: "User");

// or unnamed: bind the raw token
var adHocAdmin = await _authSteps.RegisterUserAsync(AuthDataGenerator.GenerateRegisterRequest(role: "Admin"));
await _steps.WithToken(adHocAdmin.Token.AccessToken).DeleteProductAsync(product.Id);
```

### There's more…

Per-test users are what make end-to-end journeys isolated — [chapter 7](07-end-to-end-flows.md)
uses one per journey.

## See also

- [Authentication reference](../framework/authentication.md)
