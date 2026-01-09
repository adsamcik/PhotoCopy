<#
.SYNOPSIS
    Generates geo-index files for PhotoCopy reverse geocoding.

.DESCRIPTION
    Downloads GeoNames data and builds optimized index files.

.PARAMETER TestOnly
    Generate a small test dataset (~50 cities) for unit tests.

.PARAMETER CitiesOnly  
    Use cities15000.zip instead of allCountries.zip (faster, ~40K cities).

.PARAMETER Full
    Download and process full allCountries.zip (~12M locations).

.EXAMPLE
    .\generate-geodata.ps1 -TestOnly
    
.EXAMPLE
    .\generate-geodata.ps1 -CitiesOnly
#>
param(
    [switch]$TestOnly,
    [switch]$CitiesOnly,
    [switch]$Full
)

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$generatorDir = Join-Path $scriptDir "GeoIndexGenerator"
$outputDir = Join-Path $scriptDir ".." "PhotoCopy" "data"

# Ensure output directory exists
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

Push-Location $generatorDir
try {
    Write-Host "Building GeoIndexGenerator..." -ForegroundColor Cyan
    dotnet build --configuration Release --verbosity quiet
    
    if ($TestOnly) {
        Write-Host "Generating test dataset (~50 cities)..." -ForegroundColor Green
        dotnet run --configuration Release --no-build -- --test-only --output $outputDir
    }
    elseif ($CitiesOnly) {
        Write-Host "Downloading and processing cities15000.zip..." -ForegroundColor Green
        dotnet run --configuration Release --no-build -- --download --cities-only --output $outputDir
    }
    elseif ($Full) {
        Write-Host "Downloading and processing allCountries.zip (this may take a while)..." -ForegroundColor Green
        dotnet run --configuration Release --no-build -- --download --output $outputDir
    }
    else {
        Write-Host "Usage:" -ForegroundColor Yellow
        Write-Host "  .\generate-geodata.ps1 -TestOnly     # Small test dataset"
        Write-Host "  .\generate-geodata.ps1 -CitiesOnly   # ~40K major cities"
        Write-Host "  .\generate-geodata.ps1 -Full         # Full GeoNames (~12M)"
        exit 0
    }
    
    Write-Host "`nGenerated files:" -ForegroundColor Cyan
    Get-ChildItem $outputDir -Filter "geo.*" | ForEach-Object {
        $size = if ($_.Length -gt 1MB) { "{0:F2} MB" -f ($_.Length / 1MB) } else { "{0:F2} KB" -f ($_.Length / 1KB) }
        Write-Host "  $($_.Name): $size" -ForegroundColor White
    }
}
finally {
    Pop-Location
}
