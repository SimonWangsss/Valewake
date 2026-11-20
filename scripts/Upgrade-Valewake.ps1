[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$OldModPath,
    [string]$NewModPath
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($NewModPath)) {
    $NewModPath = Split-Path -Parent $MyInvocation.MyCommand.Path
}
function Get-ContentHash([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash($stream)) }
    finally { $stream.Dispose(); $sha.Dispose() }
}
if (-not $OldModPath) {
    Add-Type -AssemblyName System.Windows.Forms
    $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
    $dialog.Description = 'Select OLD Valewake, its data folder, or the folder containing Valewake.'
    if ($dialog.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) { return }
    $OldModPath = $dialog.SelectedPath
    $dialog.Dispose()
}

function Get-ModRoot([string]$Path, [bool]$AllowDataOnly = $false) {
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        throw "Folder not found: $Path. Select an extracted folder, not the ZIP file."
    }
    $resolved = (Resolve-Path -LiteralPath $Path).Path.TrimEnd('\', '/')
    $root = $resolved
    $dataOnly = $false
    if (-not (Test-Path -LiteralPath (Join-Path $root 'manifest.json') -PathType Leaf)) {
        $candidates = @()
        if ((Split-Path $resolved -Leaf) -eq 'data') {
            $parent = Split-Path $resolved -Parent
            $candidates += $parent
            if ((Split-Path $parent -Leaf) -eq 'Backend') { $candidates += Split-Path $parent -Parent }
        }
        $candidates += @(Get-ChildItem -LiteralPath $resolved -Directory | ForEach-Object { $_.FullName })
        $matches = @($candidates | Select-Object -Unique | Where-Object {
            $manifestPath = Join-Path $_ 'manifest.json'
            if (Test-Path -LiteralPath $manifestPath -PathType Leaf) {
                try { (Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json).UniqueID -eq 'Simon.Valewake' }
                catch { $false }
            }
        })
        if ($matches.Count -gt 1) { throw 'Multiple Valewake folders found. Select the exact OLD Valewake folder, not a directory containing multiple versions.' }
        if ($matches.Count -eq 1) { $root = $matches[0] }
        elseif ($AllowDataOnly -and (Split-Path $resolved -Leaf) -eq 'data' -and
            @(@('memory', 'traces', 'custom_npcs') | Where-Object {
                Test-Path -LiteralPath (Join-Path $resolved $_) -PathType Container
            }).Count) {
            $dataOnly = $true
        }
        else { throw 'No Valewake installation found. Select Valewake (with manifest.json), its data folder, or an outer folder containing ONE Valewake installation. The NEW destination must be an extracted full package.' }
    }
    if (-not $dataOnly) {
        $manifest = Get-Content -LiteralPath (Join-Path $root 'manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($manifest.UniqueID -ne 'Simon.Valewake') { throw 'The selected manifest belongs to a different mod, not Valewake.' }
    }
    $resolved = $root
    foreach ($item in @((Get-Item -LiteralPath $resolved)) + @(Get-ChildItem -LiteralPath $resolved -Force -Recurse)) {
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked folders/files are not supported.' }
    }
    return [pscustomobject]@{ Root = $resolved; DataOnly = $dataOnly }
}

$oldSource = Get-ModRoot $OldModPath $true
$old = $oldSource.Root
$new = (Get-ModRoot $NewModPath).Root
Write-Output "OLD source: $old"
Write-Output "NEW destination: $new"
if ($oldSource.DataOnly) { Write-Output 'Data-only backup detected. Model/API settings cannot be recovered from this folder.' }
if ($old.Equals($new, [StringComparison]::OrdinalIgnoreCase) -or
    $old.StartsWith($new + '\', [StringComparison]::OrdinalIgnoreCase) -or
    $new.StartsWith($old + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Old and new folders must be separate, not nested.'
}
$running = @(Get-Process -ErrorAction SilentlyContinue | Where-Object {
    $_.ProcessName -match '^(Stardew Valley|StardewModdingAPI|ValewakeBackend|StardewAgentBackend)$'
})
if ($running.Count) { throw 'Close Stardew Valley, SMAPI and the bundled backend first.' }

$plan = New-Object 'System.Collections.Generic.List[object]'
foreach ($kind in @('memory', 'traces', 'custom_npcs')) {
    $roots = @()
    $locations = @("data\$kind", "Backend\data\$kind")
    if ($oldSource.DataOnly) { $locations = @($kind) }
    foreach ($relative in $locations) {
        $candidate = Join-Path $old $relative
        if ((Test-Path -LiteralPath $candidate) -and @(Get-ChildItem -LiteralPath $candidate -Force -Recurse -File).Count) {
            $roots += $candidate
        }
    }
    if ($roots.Count -gt 1) { throw "Both data/$kind and Backend/data/$kind contain files. Resolve the legacy split manually; no files changed." }
    if (-not $roots.Count) { continue }
    $sourceRoot = $roots[0]
    foreach ($file in Get-ChildItem -LiteralPath $sourceRoot -Force -Recurse -File) {
        $relative = $file.FullName.Substring($sourceRoot.Length + 1)
        if ($kind -eq 'custom_npcs' -and $relative -match '^(?:_|\.)') { continue }
        $destination = Join-Path (Join-Path $new "data\$kind") $relative
        if (Test-Path -LiteralPath $destination) {
            if ((Get-ContentHash $destination) -eq (Get-ContentHash $file.FullName)) { continue }
            throw "Destination conflict in data/$kind. Use a fresh package; no data was overwritten."
        }
        # Never merge an existing live Memory or Trace timeline, even with different filenames.
        if ($kind -in @('memory', 'traces')) {
            $existing = Join-Path $new "data\$kind"
            if ((Test-Path -LiteralPath $existing) -and @(Get-ChildItem -LiteralPath $existing -Force -Recurse -File).Count) {
                throw "New data/$kind is already populated. Use a fresh package; automatic timeline merging is unsafe."
            }
        }
        $plan.Add([pscustomobject]@{ Source = $file.FullName; Destination = $destination; Kind = $kind })
    }
}

$oldConfigPath = Join-Path $old 'config.json'
$newConfigPath = Join-Path $new 'config.json'
$newConfig = Get-Content -LiteralPath $newConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
$migratedKeys = @()
if (-not $oldSource.DataOnly -and (Test-Path -LiteralPath $oldConfigPath)) {
    $oldConfig = Get-Content -LiteralPath $oldConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($key in @('LlmProvider', 'LlmApiKey', 'LlmModel', 'LlmApiBase')) {
        $property = $oldConfig.PSObject.Properties[$key]
        if ($property -and $newConfig.PSObject.Properties[$key]) {
            if ($property.Value -isnot [string]) { throw "Invalid configuration type: $key" }
            $newConfig.$key = $property.Value
            $migratedKeys += $key
        }
    }
}
Write-Output ("Migration: {0} files. Model fields: {1}. Values are not printed." -f $plan.Count, ($migratedKeys -join ', '))
Write-Output 'RAG, built-in personas, meeting spots, programs, .env and runtime paths are NOT imported.'
if (-not $PSCmdlet.ShouldProcess($new, 'Back up target config and import private data from old Valewake')) { return }

# Keep backups outside Mods. The old installation is never edited or removed.
$parent = Split-Path $new -Parent
if ((Split-Path $parent -Leaf) -eq 'Mods') { $parent = Split-Path $parent -Parent }
$backup = Join-Path $parent ("ValewakeUpgradeBackups\" + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $backup -Force | Out-Null
Copy-Item -LiteralPath $newConfigPath -Destination (Join-Path $backup 'config.before.json')
$journal = [pscustomobject]@{ OldModPath = $old; NewModPath = $new; Files = @($plan.ToArray()); ConfigKeys = $migratedKeys }
$journal | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $backup 'migration.json') -Encoding UTF8
foreach ($item in $plan) {
    New-Item -ItemType Directory -Path (Split-Path $item.Destination -Parent) -Force | Out-Null
    Copy-Item -LiteralPath $item.Source -Destination $item.Destination
    if ((Get-ContentHash $item.Source) -ne (Get-ContentHash $item.Destination)) {
        throw "Copy verification failed. Old data is untouched; inspect migration.json in $backup"
    }
}
if ($migratedKeys.Count) {
    $newConfig | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $newConfigPath -Encoding UTF8
}
Write-Output "Migration completed. Backup: $backup"
Write-Output 'Keep the original game save and old folder. Start the game and verify memories before deleting any backup.'
Write-Output 'If the old API key was stored only in Backend/.env or llm_config.json, configure it again in the new mod.'
