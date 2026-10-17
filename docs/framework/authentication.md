# Authentication

Tests never handle tokens by hand. Every request built with `RequestBuilder` gets its credentials
from one place, decided in this order (the **last** auth-related builder call wins):

| # | Source | Use for |
|---|--------|---------|
| 1 | `.WithoutAuth()` — no auth header at all (neither `Authorization` nor the provider's header) | 401 tests |
| 2 | `.AsIdentity("name")` — the token stored for that identity in `TokenProvider` | per-request roles / users |
| 3 | an explicit header: `.WithBearerToken(raw)` or `.WithHeader("Authorization", ...)` | ad-hoc tokens, malformed tokens |
| 4 | the active **authentication provider** (configured once) | everything else |

- [Authentication modes (providers)](#authentication-modes)
- [Multiple identities](#multiple-identities)
- [Per-request control](#per-request-control)
- [Binding an identity to a step class](#binding-an-identity-to-a-step-class)
- [Writing a custom provider](#mode-custom)

---

## Authentication modes

The provider is selected by the `Authentication` section of `appsettings.test.json` and asked for a
header on **every** `RequestBuilder.Build()`. Secrets belong in environment variables
(`TEST_Authentication__<Key>`), never in committed JSON.

| Mode | Use case | Code you write |
|------|----------|----------------|
| `SessionToken` (default) | Your global setup logs in once and stores the token in `TokenProvider` | the login call |
| `OAuth2ClientCredentials` | IdentityServer, Auth0, Keycloak, Entra ID (machine-to-machine) — fetched, cached, refreshed | none |
| `ApiKey` | key in a header such as `X-Api-Key` | none |
| `StaticToken` | long-lived PAT / token issued out of band | none |
| `None` | anonymous APIs | none |
| `Custom` | HMAC signatures, cookies, anything else | one small class |

Common keys: `HeaderName` (default `Authorization`), `Scheme` (default `Bearer`; `""` sends the raw value).

### Mode `SessionToken`

```json
"Authentication": { "Mode": "SessionToken" }
```

```csharp
// in your [SetUpFixture] — any login flow your product needs
var response = await authClient.SendAsync<LoginResponse>(
    RequestFactory.Post("/api/v2/sessions", new { user, password }));
TokenProvider.SetToken(response.Data!.AccessToken);   // the default identity
```

The demo shop's [`GlobalSetup`](../../examples/demo-shop/api-tests/DemoShop.Tests/GlobalSetup.cs)
registers a user (falling back to login when it already exists) and stores its token.

### Mode `OAuth2ClientCredentials`

```json
"Authentication": {
  "Mode": "OAuth2ClientCredentials",
  "OAuth2": {
    "TokenUrl": "https://login.example.com/connect/token",
    "ClientId": "api-tests",
    "Scope": "inventory.read inventory.write",
    "Audience": "",
    "RefreshSkewSeconds": 30
  }
}
```

```bash
export TEST_Authentication__OAuth2__ClientSecret="<from the CI secret store>"
```

The token is requested on first use, cached, and refreshed `RefreshSkewSeconds` before it expires.

### Mode `ApiKey`

```json
"Authentication": { "Mode": "ApiKey", "HeaderName": "X-Api-Key" }
```

```bash
export TEST_Authentication__ApiKey="<secret>"
```

### Mode `StaticToken`

```json
"Authentication": { "Mode": "StaticToken", "Scheme": "Bearer" }
```

```bash
export TEST_Authentication__Token="<secret>"
```

### Mode `None`

```json
"Authentication": { "Mode": "None" }
```

### Mode `Custom`

Implement the one-method interface:

```csharp
public sealed class HmacAuthenticationProvider : IAuthenticationProvider
{
    public AuthenticationHeader? GetAuthenticationHeader()
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signature = Sign(timestamp, Environment.GetEnvironmentVariable("HMAC_SECRET")!);
        return new AuthenticationHeader("X-Signature", $"{timestamp}:{signature}");
    }
}
```

and reference it from config…

```json
"Authentication": {
  "Mode": "Custom",
  "CustomProviderType": "MyProduct.Tests.Auth.HmacAuthenticationProvider, MyProduct.Tests"
}
```

…or set it in code (overrides config; `AuthenticationContext.Reset()` returns to config):

```csharp
AuthenticationContext.Provider = new DelegateAuthenticationProvider(() => MySession.CurrentToken);
```

Returning `null` from a provider means "send no header".

## Multiple identities

`TokenProvider` stores tokens by identity name (case-insensitive). The unnamed calls use the
default identity, so suites that need only one user never see names.

| Member | Purpose |
|--------|---------|
| `SetToken(token)` / `Token` / `HasToken` | the default identity (used by `SessionToken` mode) |
| `SetToken(identity, token)` | register or replace a named identity |
| `GetToken(identity)` / `GetRequiredToken(identity)` | read (the latter throws, listing the registered names) |
| `HasTokenFor(identity)`, `RegisteredIdentities` | inspection |
| `Remove(identity)` / `Clear()` | drop one / all |

Two ways to provision identities, both shown in the demo shop:

```jsonc
// 1. once per suite, from config — GlobalSetup registers one user per entry
"Identities": [
  { "Name": "admin", "Role": "Admin" },
  { "Name": "user",  "Role": "User" }
]
```

```csharp
// 2. ad hoc, inside a test — a fresh user per test, e.g. for e2e journeys
var auth = await _authSteps.RegisterIdentityAsync($"customer-{Guid.NewGuid():N}", role: "User");
```

How identities are *created* is product-specific (register endpoint, seeded accounts, an IdP…),
so it lives in the consumer's steps and `GlobalSetup`; the framework only stores and applies tokens.

## Per-request control

```csharp
// 401 — no credentials at all
RequestBuilder.Create().WithMethod(Method.Get).WithPath("/api/products").WithoutAuth();

// as a named identity
RequestFactory.Delete("/api/products/42").AsIdentity("admin");

// with a raw or deliberately broken token
RequestFactory.Get("/api/products").WithBearerToken("not-a-jwt");
```

## Binding an identity to a step class

Steps in the example expose `AsIdentity(name)` and `WithToken(raw)`, which return a **copy** of the
step class whose every request is sent as that identity — the original instance is unchanged:

```csharp
var asUser  = await _products.AsIdentity("user").DeleteProductAsync(id);    // 403
var asAdmin = await _products.AsIdentity("admin").DeleteProductAsync(id);   // 204
```

The pattern is a few lines in each step class (a private `Authorized(builder)` helper); see
[`ProductServiceSteps`](../../examples/demo-shop/api-tests/DemoShop.Steps/ServiceSteps/ProductServiceSteps.cs)
and [tutorial chapter 6](../tutorial/06-authentication-and-identities.md).

## Notes

- Tokens show up in Allure request attachments — see [core.md](core.md#allure-reporting-and-logging).
- `TokenProvider` and `AuthenticationContext` are static: fixtures that change them
  (like the provider unit tests) must not run in parallel with others.
- The behaviour above is pinned by the unit tests in
  `framework/tests/ApiTestFramework.Framework.Tests/AuthenticationProviderTests.cs`.
