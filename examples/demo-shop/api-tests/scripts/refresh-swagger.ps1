#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Refreshes the offline swagger files of the demo shop from the RUNNING services.

.DESCRIPTION
    Downloads swagger.json from each running example service into
    DemoShop.Clients/swagger/. Start the stack first:
        docker compose -f examples/demo-shop/docker-compose.yml up -d --build --wait

    IMPORTANT: the committed swagger files are a curated contract (required
    fields, documented 401/403 responses, nullability). The live Swashbuckle
    output is less precise - review the diff and keep the curated parts.
    See docs/tutorial/02-onboard-a-service.md.

.EXAMPLE
    ./examples/demo-shop/api-tests/scripts/refresh-swagger.ps1
#>

$ErrorActionPreference = "Stop"

$services = @(
    @{ Name = "AuthService";    Url = "http://localhost:5300/swagger/v1/swagger.json"; Output = "DemoShop.Clients/swagger/auth-swagger.json" },
    @{ Name = "ProductService"; Url = "http://localhost:5100/swagger/v1/swagger.json"; Output = "DemoShop.Clients/swagger/product-swagger.json" },
    @{ Name = "OrderService";   Url = "http://localhost:5200/swagger/v1/swagger.json"; Output = "DemoShop.Clients/swagger/order-swagger.json" }
)

$root = Split-Path -Parent $PSScriptRoot   # examples/demo-shop/api-tests

foreach ($svc in $services) {
    Write-Host "Downloading $($svc.Name) swagger..." -ForegroundColor Cyan
    try {
        $outPath = Join-Path $root $svc.Output
        Invoke-WebRequest -Uri $svc.Url -OutFile $outPath -TimeoutSec 10
        Write-Host "  -> Saved to $($svc.Output)" -ForegroundColor Green
    }
    catch {
        Write-Warning "  -> Failed to download from $($svc.Url): $_"
        Write-Warning "     Make sure $($svc.Name) is running."
    }
}

Write-Host ""
Write-Host "Done. Review the diff (git diff DemoShop.Clients/swagger) before committing," -ForegroundColor Yellow
Write-Host "then run regenerate-all.ps1 to update builders, scaffolds and DTOs." -ForegroundColor Yellow
