param(
    [ValidateSet("win-x64", "win-arm64")]
    [string] $Runtime = "win-x64",

    [string] $OutputFolder = "publish"
)

$ErrorActionPreference = "Stop"

# Run correctly even when the script is launched from another folder.
$projectRoot = $PSScriptRoot
$projectFile = Join-Path $projectRoot "AlchemyCalculator\AlchemyCalculator.csproj"
$outputPath = Join-Path $projectRoot $OutputFolder

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "The .NET SDK was not found. Install the .NET 8 SDK, then run this script again."
}

if (-not (Test-Path $projectFile)) {
    throw "Could not find the project at $projectFile."
}

Push-Location $projectRoot
try {
    Write-Host "Alchemy Calculator build" -ForegroundColor Cyan
    Write-Host "Runtime: $Runtime"
    Write-Host "Output:  $outputPath"
    Write-Host ""

    Write-Host "Restoring packages..." -ForegroundColor DarkCyan
    dotnet restore $projectFile -r $Runtime

    Write-Host ""
    Write-Host "Publishing a self-contained Windows app..." -ForegroundColor DarkCyan
    dotnet publish $projectFile `
        -c Release `
        -r $Runtime `
        --self-contained true `
        --no-restore `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=None `
        -o $outputPath

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }

    $exePath = Join-Path $outputPath "KabanCalculator.exe"
    Write-Host ""
    if (Test-Path $exePath) {
        Write-Host "Build complete:" -ForegroundColor Green
        Write-Host $exePath -ForegroundColor Green
    } else {
        throw "Publish finished, but the expected executable was not found at $exePath."
    }
}
finally {
    Pop-Location
}