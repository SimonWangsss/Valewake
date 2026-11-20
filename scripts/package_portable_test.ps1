param([string]$Label = "20261017-test")

$ErrorActionPreference = "Stop"
if ($Label -notmatch '^[a-zA-Z0-9-]+$') { throw "Use a simple ASCII package label." }
$root = Split-Path -Parent $PSScriptRoot
$backend = Join-Path $root "backend"
$python = Join-Path $backend ".venv\Scripts\python.exe"
$stamp = Get-Date -Format "yyyyMMdd_HHmmss"
$output = Join-Path $root "outputs\portable_packages\$Label-$stamp"
$stage = Join-Path $output "stage\Valewake"
New-Item -ItemType Directory -Path $stage -Force | Out-Null

Push-Location $backend
try {
    & $python -m PyInstaller --noconfirm ValewakeBackend.spec
    if ($LASTEXITCODE -ne 0) { throw "Backend packaging failed." }
} finally { Pop-Location }
Copy-Item -LiteralPath (Join-Path $backend "dist\ValewakeBackend.exe") -Destination (Join-Path $root "Valewake\Backend\ValewakeBackend.exe") -Force
& dotnet build (Join-Path $root "Valewake\Valewake.csproj") -c Release -p:EnableModDeploy=false
if ($LASTEXITCODE -ne 0) { throw "Mod build failed." }

# Explicit allowlist: never copy a live installation or the entire build directory.
$build = Join-Path $root "Valewake\bin\Release\net6.0"
foreach ($name in @("Valewake.dll", "Valewake.deps.json", "manifest.json")) {
    Copy-Item -LiteralPath (Join-Path $build $name) -Destination $stage
}
$config = Get-Content -LiteralPath (Join-Path $root "Valewake\config.json") -Raw | ConvertFrom-Json
$config.LlmApiKey = ""
$config.AutoStartBackend = $true
$config.BackendExecutablePath = "Backend\ValewakeBackend.exe"
$config.BackendWorkingDirectory = "Backend"
$config.BackendUrl = "http://127.0.0.1:8010/chat"
$config.BackendHealthUrl = "http://127.0.0.1:8010/health"
$config | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $stage "config.json") -Encoding UTF8
New-Item -ItemType Directory -Path (Join-Path $stage "Backend") -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $backend "dist\ValewakeBackend.exe") -Destination (Join-Path $stage "Backend")
Copy-Item -LiteralPath (Join-Path $backend ".env.example") -Destination (Join-Path $stage "Backend")
Copy-Item -LiteralPath (Join-Path $backend "providers") -Destination (Join-Path $stage "Backend") -Recurse
New-Item -ItemType Directory -Path (Join-Path $stage "data") -Force | Out-Null
foreach ($name in @("personas", "rag")) {
    Copy-Item -LiteralPath (Join-Path $backend "data\$name") -Destination (Join-Path $stage "data") -Recurse
}
New-Item -ItemType Directory -Path (Join-Path $stage "data\custom_npcs") -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $backend "data\custom_npcs\_template") -Destination (Join-Path $stage "data\custom_npcs") -Recurse
Copy-Item -LiteralPath (Join-Path $root "Valewake\data\meeting_spots.json") -Destination (Join-Path $stage "data")
Copy-Item -LiteralPath (Join-Path $root "docs\portable_test_install_zh.md") -Destination (Join-Path $stage "INSTALL_zh.md")
Copy-Item -LiteralPath (Join-Path $root "CHANGELOG.md") -Destination (Join-Path $stage "CHANGELOG.md")
Copy-Item -LiteralPath (Join-Path $root "docs\release_notes_v1.1.0.md") -Destination (Join-Path $stage "RELEASE_NOTES.md")
Copy-Item -LiteralPath (Join-Path $root "docs\custom_npc_packs_zh.md") -Destination (Join-Path $stage "CUSTOM_NPCS_zh.md")
Copy-Item -LiteralPath (Join-Path $root "scripts\Upgrade-Valewake.ps1") -Destination $stage
Copy-Item -LiteralPath (Join-Path $root "scripts\Upgrade-Valewake.cmd") -Destination $stage
Copy-Item -LiteralPath (Join-Path $root "docs\upgrading_zh.md") -Destination (Join-Path $stage "UPGRADE_zh.md")

$files = @(Get-ChildItem -LiteralPath $stage -Recurse -Force -File)
$forbidden = @($files | Where-Object {
    $_.Name -eq ".env" -or $_.Name -eq "StardewAgentBackend.exe" -or
    $_.FullName -match '[\\/](memory|traces|test_sets|__pycache__)[\\/]' -or
    $_.Extension -in @(".log", ".pyc", ".zip")
})
if ($forbidden.Count -gt 0) { throw "Unexpected private or obsolete files in staging." }
$manifest = @($files | ForEach-Object {
    [pscustomobject]@{ path = $_.FullName.Substring($stage.Length + 1); bytes = $_.Length;
        sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
})
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output "files.json") -Encoding UTF8
$zip = Join-Path $output "Valewake-$Label.zip"
# .NET archive API includes .env.example reliably, unlike wildcard-based packaging.
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory((Split-Path $stage -Parent), $zip)
$archive = [System.IO.Compression.ZipFile]::OpenRead($zip)
try {
    $names = @($archive.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
    foreach ($required in @("Valewake/Valewake.dll", "Valewake/Backend/ValewakeBackend.exe", "Valewake/Backend/.env.example", "Valewake/data/meeting_spots.json", "Valewake/data/rag/stardew/corpus.json", "Valewake/Upgrade-Valewake.cmd", "Valewake/Upgrade-Valewake.ps1", "Valewake/UPGRADE_zh.md")) {
        if ($required -notin $names) { throw "Missing archive member: $required" }
    }
    if ($archive.Entries.Count -ne $files.Count) { throw "Archive file count mismatch." }
} finally { $archive.Dispose() }
Get-Item -LiteralPath $zip | Select-Object FullName, Length
Get-FileHash -LiteralPath $zip -Algorithm SHA256
Write-Output "Staging: $stage"
