$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '发布配置.ps1')
# Synthetic files under the project; never mutate published binaries.
$fixture = Join-Path $WorkspaceRoot ('.tools/package-tests/' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture -Force | Out-Null
$ReleaseRoot = Join-Path $fixture 'release'
$PublishManifest = Join-Path $fixture 'manifest.json'
New-Item -ItemType Directory -Path $ReleaseRoot | Out-Null
Set-Content -LiteralPath (Join-Path $ReleaseRoot 'app.dll') -Value 'synthetic'
Set-Content -LiteralPath (Join-Path $ReleaseRoot 'private.json') -Value 'must not ship'
$record = @{ Path = 'app.dll'; SHA256 = (Get-FileHash -LiteralPath (Join-Path $ReleaseRoot 'app.dll')).Hash }
function Write-FixtureManifest($Record) {
    @{ Version = $ReleaseVersion; Files = @($Record) } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $PublishManifest
}
Write-FixtureManifest $record
$files = @(Get-PublishedFiles)
if ($files.Count -ne 1 -or $files[0].Path -ne 'app.dll') { throw 'Unlisted file entered publication.' }
Set-Content -LiteralPath (Join-Path $ReleaseRoot 'app.dll') -Value 'changed'
$rejected = $false
try { $null = @(Get-PublishedFiles) } catch { $rejected = $true }
if (-not $rejected) { throw 'Modified binary was accepted.' }
Write-FixtureManifest @{ Path = '../escape'; SHA256 = 'invalid' }
$rejected = $false
try { $null = @(Get-PublishedFiles) } catch { $rejected = $true }
if (-not $rejected) { throw 'Traversal was accepted.' }
Write-Output 'PASS package manifest: exclude unlisted files, reject tampering and traversal'
