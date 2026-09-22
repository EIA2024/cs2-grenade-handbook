param([switch]$Published)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'scripts/发布配置.ps1')
$executable = if ($Published) { Join-Path $ReleaseRoot '道具手册.exe' } else { Join-Path $PSScriptRoot 'src/Handbook.Desktop/bin/Release/net10.0-windows/道具手册.exe' }
if (-not (Test-Path -LiteralPath $executable)) { throw '请先构建软件。' }
# This script is explicitly a user-operated, visible interactive test, not a background helper.
Start-Process -FilePath $executable -ArgumentList '--input-test'


