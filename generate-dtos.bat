@echo off
REM ============================================================
REM Generate DTO files from offline OpenAPI swagger specs.
REM
REM Output: tests/ApiTestFramework.Clients/Dtos/Generated/
REM
REM Prerequisites: build the CLI project first:
REM   dotnet build tests/ApiTestFramework.OpenApi.Cli
REM
REM Shared types (AuditInfo) live in CommonDtos.g.cs and are
REM skipped when generating service-specific files.
REM ============================================================

set CLI=tests\ApiTestFramework.OpenApi.Cli
set SWAGGER=tests\ApiTestFramework.Clients\swagger
set OUT=tests\ApiTestFramework.Clients\Dtos\Generated
set COMMON_NS=ApiTestFramework.Clients.Common

echo Building CLI tool...
dotnet build %CLI% -c Release -v quiet

echo.
echo Generating AuthService DTOs...
dotnet run --project %CLI% --no-build -c Release -- ^
  --mode dto ^
  --swagger %SWAGGER%\auth-swagger.json ^
  --ns ApiTestFramework.Clients.AuthService ^
  --out %OUT%\AuthDtos.g.cs

echo.
echo Generating ProductService DTOs...
dotnet run --project %CLI% --no-build -c Release -- ^
  --mode dto ^
  --swagger %SWAGGER%\product-swagger.json ^
  --ns ApiTestFramework.Clients.ProductService ^
  --skip AuditInfo ^
  --common-ns %COMMON_NS% ^
  --out %OUT%\ProductDtos.g.cs

echo.
echo Generating OrderService DTOs...
dotnet run --project %CLI% --no-build -c Release -- ^
  --mode dto ^
  --swagger %SWAGGER%\order-swagger.json ^
  --ns ApiTestFramework.Clients.OrderService ^
  --skip AuditInfo ^
  --common-ns %COMMON_NS% ^
  --out %OUT%\OrderDtos.g.cs

echo.
echo Generating Common DTOs (AuditInfo) from ProductService swagger...
dotnet run --project %CLI% --no-build -c Release -- ^
  --mode dto ^
  --swagger %SWAGGER%\product-swagger.json ^
  --ns ApiTestFramework.Clients.Common ^
  --skip Product,PriceInfo,InventoryInfo,CreateProductRequest,UpdateProductRequest ^
  --out %OUT%\CommonDtos.g.cs

echo.
echo Generating ImportService DTOs...
dotnet run --project %CLI% --no-build -c Release -- ^
  --mode dto ^
  --swagger %SWAGGER%\import-swagger.json ^
  --ns ApiTestFramework.Clients.ImportService ^
  --out %OUT%\ImportDtos.g.cs

echo.
echo Done! Generated DTOs are in %OUT%
pause
