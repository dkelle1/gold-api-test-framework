#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Refreshes swagger.json files from running microservices.
    Run this after changing API endpoints or response models.

.DESCRIPTION
    Downloads the swagger.json from each running service and overwrites
    the offline swagger files used by NSwag for client generation.
    After running this script, run generate-clients.bat to regenerate clients.

.EXAMPLE
    .\scripts\refresh-swagger.ps1
#>

$ErrorActionPreference = "Stop"

$services = @(
    @{ Name = "AuthService";    Url = "http://localhost:5300/swagger/v1/swagger.json"; Output = "tests\ApiTestFramework.Clients\swagger\auth-swagger.json" },
    @{ Name = "ProductService"; Url = "http://localhost:5100/swagger/v1/swagger.json"; Output = "tests\ApiTestFramework.Clients\swagger\product-swagger.json" },
    @{ Name = "OrderService";   Url = "http://localhost:5200/swagger/v1/swagger.json"; Output = "tests\ApiTestFramework.Clients\swagger\order-swagger.json" }
)

$root = Split-Path -Parent $PSScriptRoot

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
Write-Host "Done. Now run 'generate-clients.bat' to regenerate NSwag clients." -ForegroundColor Yellow
