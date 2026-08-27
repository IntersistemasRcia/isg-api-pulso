# Convenience script: find the project file and run it in Development mode
# Usage: Open PowerShell in the repository root (this file's folder) and run: .\run.ps1

$ErrorActionPreference = 'Stop'
Write-Host "Searching for .csproj under" (Get-Location)

# Prefer the main project by name if present
$proj = Get-ChildItem -Path . -Recurse -Filter 'isg-api-pulso.csproj' -File -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $proj) {
    # fallback: any csproj found
    $proj = Get-ChildItem -Path . -Recurse -Filter '*.csproj' -File | Select-Object -First 1
}

if (-not $proj) {
    Write-Error "No .csproj file found under current directory. Ensure you are in the repository root."
    exit 1
}

$projPath = $proj.FullName
Write-Host "Using project: $projPath"

# Set environment and run
$env:ASPNETCORE_ENVIRONMENT = 'Development'
Write-Host "Starting project in Development mode..."
Write-Host "dotnet run --project `"$projPath`" --no-launch-profile --urls 'https://localhost:44351;http://localhost:5178'"

dotnet run --project "$projPath" --no-launch-profile --urls "https://localhost:44351;http://localhost:5178"
