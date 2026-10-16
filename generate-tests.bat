@echo off
:: generate-tests.bat
:: Generates verb-specific NUnit test stubs from offline swagger specs.
:: Output goes to artifacts\generated-tests\ so the scaffold does not get compiled automatically.
::
:: Usage: generate-tests.bat

setlocal

set CLI=dotnet run --project tests\ApiTestFramework.OpenApi.Cli\ApiTestFramework.OpenApi.Cli.csproj --
set SWAGGER=tests\ApiTestFramework.Clients\swagger
set OUT=artifacts\generated-tests
set NS=ApiTestFramework.Tests.Generated

if not exist %OUT% mkdir %OUT%

echo -------------------------------------------------------
echo Generating Product test scaffolds...
%CLI% --mode tests --swagger %SWAGGER%\product-swagger.json ^
      --service Product ^
      --ns      %NS% ^
      --out     %OUT%\ProductGeneratedTests.g.cs

echo -------------------------------------------------------
echo Generating Order test scaffolds...
%CLI% --mode tests --swagger %SWAGGER%\order-swagger.json ^
      --service Order ^
      --ns      %NS% ^
      --out     %OUT%\OrderGeneratedTests.g.cs

echo -------------------------------------------------------
echo Generating Auth test scaffolds...
%CLI% --mode tests --swagger %SWAGGER%\auth-swagger.json ^
      --service Auth ^
      --ns      %NS% ^
      --out     %OUT%\AuthGeneratedTests.g.cs

echo -------------------------------------------------------
echo Generating Import test scaffolds...
%CLI% --mode tests --swagger %SWAGGER%\import-swagger.json ^
      --service Import ^
      --ns      %NS% ^
      --out     %OUT%\ImportGeneratedTests.g.cs

echo -------------------------------------------------------
echo Done. Generated files are in %OUT%\
endlocal
