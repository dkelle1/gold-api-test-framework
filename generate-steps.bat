@echo off
:: generate-steps.bat
:: Generates *ServiceSteps.cs files from offline swagger specs.
:: Run from the repository root.
::
:: Usage: generate-steps.bat
::   Regenerates all three service step files and writes them to
::   tests\ApiTestFramework.Steps\ServiceSteps\Generated\

setlocal

set CLI=dotnet run --project tests\ApiTestFramework.OpenApi.Cli\ApiTestFramework.OpenApi.Cli.csproj --
set SWAGGER=tests\ApiTestFramework.Clients\swagger
set OUT=tests\ApiTestFramework.Steps\ServiceSteps\Generated
set NS=ApiTestFramework.Steps.ServiceSteps.Generated

echo -------------------------------------------------------
echo Generating ProductServiceSteps...
%CLI% --swagger %SWAGGER%\product-swagger.json ^
      --service Product --dto Product ^
      --ns      %NS% ^
      --dto-ns  ApiTestFramework.Clients.ProductService ^
      --out     %OUT%\ProductServiceSteps.g.cs

echo -------------------------------------------------------
echo Generating OrderServiceSteps...
%CLI% --swagger %SWAGGER%\order-swagger.json ^
      --service Order --dto Order ^
      --ns      %NS% ^
      --dto-ns  ApiTestFramework.Clients.OrderService ^
      --out     %OUT%\OrderServiceSteps.g.cs

echo -------------------------------------------------------
echo Generating AuthServiceSteps...
%CLI% --swagger %SWAGGER%\auth-swagger.json ^
      --service Auth --dto AuthResponse ^
      --ns      %NS% ^
      --dto-ns  ApiTestFramework.Clients.AuthService ^
      --out     %OUT%\AuthServiceSteps.g.cs

echo -------------------------------------------------------
echo Generating ImportServiceSteps...
%CLI% --swagger %SWAGGER%\import-swagger.json ^
      --service Import --dto ImportStatusResponse ^
      --ns      %NS% ^
      --dto-ns  ApiTestFramework.Clients.ImportService ^
      --out     %OUT%\ImportServiceSteps.g.cs

echo -------------------------------------------------------
echo Done. Generated files are in %OUT%\
endlocal
