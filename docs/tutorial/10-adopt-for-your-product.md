# Chapter 10 — Adopting the framework for your product

You have seen every piece working on the demo shop. This chapter sets up the same structure for
your own APIs. The framework (`framework/`, `tools/`, `scripts/ci/`) is used **as is**; everything
you write is configuration plus three small projects.

In this chapter:

- [Choosing how to consume the framework](#choosing-how-to-consume-the-framework)
- [Creating your test projects](#creating-your-test-projects)
- [Writing the global setup](#writing-the-global-setup)
- [Wiring generators, tools and CI](#wiring-generators-tools-and-ci)
- [Removing the example](#removing-the-example)
- [Checklist](#checklist)

---

## Choosing how to consume the framework

| Option | How | Good for |
|--------|-----|----------|
| **Template repository** | Copy/fork this repository, add your projects next to `examples/`, delete the example later | a dedicated test repository per product |
| **Git submodule** | `git submodule add <this repo> test-framework` in your product repository; reference `test-framework/framework/src/...` projects; run `test-framework/tools/*.py` with your `OPENAPI_TOOLS_CONFIG` | tests living next to the product code |
| **Internal NuGet** | `dotnet pack framework/src/ApiTestFramework.Core` (and `.OpenApi`) into your feed | many products, versioned framework upgrades |

The rest of the chapter assumes the template option with your projects in `my-product/api-tests/`;
for the others only the relative paths change.

---

## Creating your test projects

### How to do it…

1. Create three projects (names are yours):

   ```bash
   dotnet new classlib -n MyProduct.Clients -o my-product/api-tests/MyProduct.Clients -f net8.0
   dotnet new classlib -n MyProduct.Steps   -o my-product/api-tests/MyProduct.Steps   -f net8.0
   dotnet new nunit    -n MyProduct.Tests   -o my-product/api-tests/MyProduct.Tests   -f net8.0
   dotnet sln ApiTestFramework.sln add --solution-folder my-product my-product/api-tests/*/*.csproj
   ```

2. References — the Steps and Tests projects reference the framework core:

   ```xml
   <!-- MyProduct.Steps.csproj -->
   <ItemGroup>
     <ProjectReference Include="..\..\..\framework\src\ApiTestFramework.Core\ApiTestFramework.Core.csproj" />
     <ProjectReference Include="..\MyProduct.Clients\MyProduct.Clients.csproj" />
     <PackageReference Include="Bogus" Version="35.6.1" />
     <PackageReference Include="Allure.NUnit" Version="2.12.1" />
     <PackageReference Include="RestSharp" Version="112.1.0" />
   </ItemGroup>

   <!-- MyProduct.Tests.csproj — replace the template's test packages with these versions:
        the framework is built on NUnit 4, and the nunit template may add NUnit 3 (NU1605 downgrade error) -->
   <ItemGroup>
     <ProjectReference Include="..\..\..\framework\src\ApiTestFramework.Core\ApiTestFramework.Core.csproj" />
     <ProjectReference Include="..\MyProduct.Steps\MyProduct.Steps.csproj" />
     <PackageReference Include="NUnit" Version="4.2.2" />
     <PackageReference Include="NUnit3TestAdapter" Version="4.6.0" />
     <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
     <PackageReference Include="Allure.NUnit" Version="2.12.1" />
     <PackageReference Include="FluentAssertions" Version="7.0.0" />
     <PackageReference Include="Autofac" Version="8.1.1" />
     <PackageReference Include="Serilog.Sinks.Console" Version="6.0.0" />
     <Content Include="appsettings.test.json" CopyToOutputDirectory="PreserveNewest" />
   </ItemGroup>
   ```

   Keep FluentAssertions on 7.x — 8.x requires a commercial license.

3. Fill the Clients project: contracts in `swagger/`, DTOs, `Routes.cs`
   ([chapter 2](02-onboard-a-service.md)).

4. Add `MyProduct.Tests/appsettings.test.json`:

   ```json
   {
     "Services": {
       "IdentityService":  { "BaseUrl": "http://localhost:7000" },
       "InventoryService": { "BaseUrl": "http://localhost:7100" }
     },
     "Authentication": { "Mode": "SessionToken" },
     "Identities": [ { "Name": "admin", "Role": "Admin" } ],
     "DefaultTimeoutSeconds": 30,
     "RetryCount": 0
   }
   ```

   With `OAuth2ClientCredentials`, `ApiKey` or `StaticToken` you need no login code at all
   ([authentication reference](../framework/authentication.md)).

---

## Writing the global setup

### How to do it…

Create `MyProduct.Tests/GlobalSetup.cs` in the root namespace of the test project:

```csharp
using ApiTestFramework.Core.Auth;
using ApiTestFramework.Core.Client;
using ApiTestFramework.Core.Configuration;
using ApiTestFramework.Core.DI;
using Autofac;
using NUnit.Framework;
using Serilog;

namespace MyProduct.Tests;

[SetUpFixture]
public class GlobalSetup
{
    [OneTimeSetUp]
    public async Task BeforeAllTests()
    {
        Log.Logger = new LoggerConfiguration().MinimumLevel.Information().WriteTo.Console().CreateLogger();

        var config = ConfigurationProvider.GetTestConfiguration();
        ContainerProvider.Initialize(builder => builder.RegisterModule(new ApiClientModule(config)));

        // SessionToken mode only — replace with your product's login flow
        var identity = ContainerProvider.ResolveNamed<ApiClient>("IdentityService");
        var login = await identity.SendAsync<LoginResponse>(
            RequestFactory.Post("/api/login", new
            {
                username = "api-tests",
                password = Environment.GetEnvironmentVariable("TEST_API_PASSWORD")
            }).WithoutAuth());
        TokenProvider.SetToken(login.Data?.AccessToken
            ?? throw new InvalidOperationException($"Login failed: {(int)login.StatusCode} {login.Content}"));

        // Optional: one token per configured identity (how to create them is product-specific)
        // foreach (var id in config.Identities) TokenProvider.SetToken(id.Name, await LoginAsAsync(id));
    }

    [OneTimeTearDown]
    public void AfterAllTests()
    {
        TokenProvider.Clear();
        ContainerProvider.Dispose();
        Log.CloseAndFlush();
    }
}
```

### How it works…

A `[SetUpFixture]` applies to all tests in its namespace and below — keep fixtures in sub-namespaces
of `MyProduct.Tests`. The demo shop's version (with register-or-login and identities) is a complete
example: [`DemoShop.Tests/GlobalSetup.cs`](../../examples/demo-shop/api-tests/DemoShop.Tests/GlobalSetup.cs).

---

## Wiring generators, tools and CI

### How to do it…

1. Copy `examples/demo-shop/api-tests/generator.json` to `my-product/api-tests/`, replace the
   services and the `DemoShop` namespaces, then generate builders and scaffolds:

   ```bash
   dotnet run --project framework/src/ApiTestFramework.Generator.Cli -- --config my-product/api-tests/generator.json
   ```

2. Copy `openapi-tools.json` the same way; run the tools with
   `OPENAPI_TOOLS_CONFIG=my-product/api-tests/openapi-tools.json`.

3. Write steps ([chapter 4](04-write-steps.md)), promote scaffolds ([chapter 5](05-scaffold-and-promote-tests.md)),
   add flows ([chapter 7](07-end-to-end-flows.md)).

4. In `.github/workflows/tests.yml` change the `env` block:

   ```yaml
   EXAMPLE_TESTS: my-product/api-tests/MyProduct.Tests/MyProduct.Tests.csproj
   GENERATOR_CONFIG: my-product/api-tests/generator.json
   ```

   - If your system can run in Docker Compose, point `EXAMPLE_COMPOSE` at your compose file.
   - If it runs elsewhere, replace the *Start/Stop services* steps with `TEST_Services__<Name>__BaseUrl`
     variables (and secrets) pointing at a test environment.
   - Set `OPENAPI_TOOLS_CONFIG` for the Python steps, or drop them.

---

## Removing the example

### How to do it…

Once your own suite runs green in CI:

```bash
dotnet sln ApiTestFramework.sln remove examples/demo-shop/api-tests/*/*.csproj examples/demo-shop/services/*/*.csproj
git rm -r examples/demo-shop
```

Then change `DEFAULT_CONFIG` in `tools/openapi_common.py` to your `openapi-tools.json` (or always
set `OPENAPI_TOOLS_CONFIG`), and remove the demo-specific lines from `Jenkinsfile` if you use it.

### How it works…

Nothing in `framework/`, `tools/` or `scripts/ci/` references the example — CI's
*generated code is up to date* gate and the framework unit tests keep working unchanged.

---

## Checklist

- [ ] Projects `*.Clients`, `*.Steps`, `*.Tests` created and referencing `ApiTestFramework.Core`
- [ ] Contracts committed and reviewed (required fields, 401/403, enums as on the wire)
- [ ] DTOs (NSwag or hand-written) and route constants
- [ ] `appsettings.test.json`: services, authentication mode, identities; secrets only via `TEST_…` variables
- [ ] `GlobalSetup` initializing the container (and logging in for `SessionToken`)
- [ ] `generator.json` → builders + scaffolds generated and committed; custom partials for domain rules
- [ ] One step class per service; happy-path + `Try…` variants
- [ ] Scaffolds promoted for every operation that matters; e2e flows for the key journeys
- [ ] `openapi-tools.json` → collections, k6, smoke as needed
- [ ] CI workflow pointing at your projects; both jobs required on the main branch
- [ ] Example removed (optional, once green)

## See also

- [Architecture](../architecture.md) — the dependency rule your projects must follow
