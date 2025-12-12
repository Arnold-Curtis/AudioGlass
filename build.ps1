# Build script for TransparencyMode
# Usage: .\build.ps1 [-Configuration Release|Debug] [-Publish]

param(
    [string]$Configuration = "Release",
    [switch]$Publish
)

Write-Host "=== TransparencyMode Build Script ===" -ForegroundColor Cyan
Write-Host ""

# Restore packages
Write-Host "Restoring NuGet packages..." -ForegroundColor Yellow
dotnet restore TransparencyMode.sln
if ($LASTEXITCODE -ne 0) {
    Write-Host "Restore failed!" -ForegroundColor Red
    exit 1
}

# Build solution
Write-Host ""
Write-Host "Building solution ($Configuration)..." -ForegroundColor Yellow
dotnet build TransparencyMode.sln -c $Configuration
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}

# Publish if requested
if ($Publish) {
    Write-Host ""
    Write-Host "Publishing single-file executable..." -ForegroundColor Yellow
    
    # Clean publish directory
    if (Test-Path "publish") {
        Remove-Item "publish" -Recurse -Force
    }
    
    dotnet publish src/TransparencyMode.App/TransparencyMode.App.csproj `
        -c $Configuration `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:PublishTrimmed=false `
        -o publish/
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Publish failed!" -ForegroundColor Red
        exit 1
    }
    
    Write-Host ""
    Write-Host "Published files:" -ForegroundColor Green
    Get-ChildItem publish/*.exe | ForEach-Object {
        $sizeMB = [math]::Round($_.Length / 1MB, 2)
        Write-Host "  $($_.Name) - $sizeMB MB" -ForegroundColor White
    }
    
    Write-Host ""
    Write-Host "Executable location: publish\TransparencyMode.App.exe" -ForegroundColor Green
}

Write-Host ""
Write-Host "=== Build completed successfully ===" -ForegroundColor Green
Write-Host ""

if (!$Publish) {
    Write-Host "Tip: Run with -Publish flag to create single-file executable" -ForegroundColor Cyan
    Write-Host "Example: .\build.ps1 -Configuration Release -Publish" -ForegroundColor Gray
}
