#!/usr/bin/env pwsh
<#
.SYNOPSIS
    One-stop regeneration pipeline after API endpoints or DTOs change.

.DESCRIPTION
    Chains the regeneration steps in the right order:

      1. (optional, -RefreshSwagger) Download fresh swagger.json files from the
         RUNNING services -> tests/ApiTestFramework.Clients/swagger/
      2. (optional, -NSwag) Regenerate NSwag clients/DTOs from the offline
         swagger files (requires: dotnet tool install -g NSwag.ConsoleCore)
      3. Regenerate test-data builders (*.g.cs) and test scaffolds (*.cs.txt)
         from the offline swagger files via ApiTestFramework.Generator.Cli
      4. Build the solution to verify everything still compiles

    Hand-written builder customizations (tests/ApiTestFramework.Steps/Builders/Custom/*)
    are partial classes and are never touched by regeneration.

.EXAMPLE
    # Full pipeline against running services:
    .\scripts\regenerate-all.ps1 -RefreshSwagger -NSwag

    # Builders + scaffolds only (offline, most common during test development):
    .\scripts\regenerate-all.ps1
#>
param(
    [switch]$RefreshSwagger,
    [switch]$NSwag
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

# --- 1. Refresh swagger from running services -------------------------------
if ($RefreshSwagger) {
    Write-Host "== Step 1: refreshing swagger.json from running services" -ForegroundColor Cyan
    & (Join-Path $PSScriptRoot "refresh-swagger.ps1")
}
else {
    Write-Host "== Step 1: skipped (use -RefreshSwagger to pull from running services)" -ForegroundColor DarkGray
}

# --- 2. NSwag clients --------------------------------------------------------
if ($NSwag) {
    Write-Host "== Step 2: regenerating NSwag clients" -ForegroundColor Cyan
    Push-Location (Join-Path $root "tests\ApiTestFramework.Clients")
    try {
        foreach ($config in "nswag-auth.nswag", "nswag-product.nswag", "nswag-order.nswag") {
            Write-Host "   nswag run $config" -ForegroundColor Gray
            nswag run $config /runtime:Net80
            if ($LASTEXITCODE -ne 0) { throw "NSwag failed for $config" }
        }
    }
    finally { Pop-Location }
}
else {
    Write-Host "== Step 2: skipped (use -NSwag to regenerate DTO clients)" -ForegroundColor DarkGray
}

# --- 3. Builders + test scaffolds -------------------------------------------
Write-Host "== Step 3: regenerating test-data builders and test scaffolds" -ForegroundColor Cyan
dotnet run --project (Join-Path $root "tests/ApiTestFramework.Generator.Cli") -- $root
if ($LASTEXITCODE -ne 0) { throw "Generator CLI failed" }

# --- 4. Verify ---------------------------------------------------------------
Write-Host "== Step 4: building solution" -ForegroundColor Cyan
dotnet build (Join-Path $root "ApiTestFramework.sln") -v q --nologo
if ($LASTEXITCODE -ne 0) { throw "Build failed after regeneration — check generated code vs DTOs" }

Write-Host ""
Write-Host "Regeneration complete." -ForegroundColor Green
Write-Host "  - Builders:  tests/ApiTestFramework.Steps/Builders/<Service>/Generated/*.g.cs"
Write-Host "  - Custom:    tests/ApiTestFramework.Steps/Builders/Custom/*.cs (untouched)"
Write-Host "  - Scaffolds: tests/ApiTestFramework.Tests/Generated/*.cs.txt (promote to .cs by hand)"
