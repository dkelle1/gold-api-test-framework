@echo off
REM ============================================================
REM Generate NSwag clients from OFFLINE swagger.json files.
REM Uses static swagger files in swagger/ folder — no running services needed.
REM
REM Prerequisites:
REM   NSwag CLI: dotnet tool install -g NSwag.ConsoleCore
REM
REM To refresh swagger.json files from running services, run:
REM   scripts\refresh-swagger.ps1
REM ============================================================

echo Generating AuthService client...
nswag run nswag-auth.nswag /runtime:Net80

echo Generating ProductService client...
nswag run nswag-product.nswag /runtime:Net80

echo Generating OrderService client...
nswag run nswag-order.nswag /runtime:Net80

echo Done! Generated clients are in the Generated/ folder.
pause
