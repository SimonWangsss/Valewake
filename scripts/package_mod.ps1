param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$backend = Join-Path $root "backend"
$python = Join-Path $backend ".venv\Scripts\python.exe"
$bundle = Join-Path $root "Valewake\Backend"

if (-not (Test-Path -LiteralPath $python)) {
    throw "Backend virtual environment not found at $python"
}

Push-Location $backend
try {
    & $python -m PyInstaller --noconfirm --clean --onefile --name ValewakeBackend backend_launcher.py
    if ($LASTEXITCODE -ne 0) { throw "PyInstaller failed with exit code $LASTEXITCODE" }
}
finally {
    Pop-Location
}

New-Item -ItemType Directory -Path $bundle -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $backend "dist\ValewakeBackend.exe") -Destination $bundle -Force
Copy-Item -LiteralPath (Join-Path $backend ".env.example") -Destination $bundle -Force
$personasBundle = Join-Path $bundle "data\personas"
$ragBundle = Join-Path $bundle "data\rag"
New-Item -ItemType Directory -Path $personasBundle -Force | Out-Null
New-Item -ItemType Directory -Path $ragBundle -Force | Out-Null
Copy-Item -Path (Join-Path $backend "data\personas\*") -Destination $personasBundle -Recurse -Force
Copy-Item -Path (Join-Path $backend "data\rag\*") -Destination $ragBundle -Recurse -Force

& dotnet build (Join-Path $root "Valewake\Valewake.csproj") -c $Configuration
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE" }
