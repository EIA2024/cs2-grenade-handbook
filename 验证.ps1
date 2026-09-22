param([switch]$Published)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'scripts/发布配置.ps1')
& (Join-Path $PSScriptRoot 'scripts/检查边界.ps1')
& (Join-Path $PSScriptRoot 'scripts/验证发布边界.ps1')
$executable = if ($Published) { Join-Path $ReleaseRoot '道具手册.exe' } else { Join-Path $PSScriptRoot 'src/Handbook.Desktop/bin/Release/net10.0-windows/道具手册.exe' }
if (-not (Test-Path -LiteralPath $executable)) { throw '请先运行 构建.ps1' }
if ($Published) { $null = @(Get-PublishedFiles) }
$before = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot '测试结果') -Directory -ErrorAction SilentlyContinue).FullName
$testProcess = Start-Process -FilePath $executable -ArgumentList '--self-test' -WorkingDirectory $PSScriptRoot -WindowStyle Hidden -Wait -PassThru
$newResults = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot '测试结果') -Directory | Where-Object { $_.FullName -notin $before })
if ($newResults.Count -ne 1) { throw '本次测试必须生成唯一新记录，不能复用旧日志。' }
$latest = $newResults[0]
$log = Get-Content -LiteralPath (Join-Path $latest.FullName '验证结果.txt') -Raw
Write-Output $log
Write-Output ('证据目录：' + $latest.FullName)
if ($testProcess.ExitCode -ne 0 -or $log -notmatch 'RESULT: \d+ passed, 0 failed') { throw ('验证未通过，退出码：' + $testProcess.ExitCode) }
$hashes = @{}
foreach ($name in @('道具手册.exe', '道具手册.dll', 'Handbook.Core.dll', 'Handbook.Storage.dll')) {
    $hashes[$name] = (Get-FileHash -LiteralPath (Join-Path (Split-Path -Parent $executable) $name)).Hash
}
$manifestHash = $null
if ($Published) { $null = @(Get-PublishedFiles); $manifestHash = (Get-FileHash -LiteralPath $PublishManifest).Hash }
@{ Published = [bool]$Published; VerifiedAt = (Get-Date).ToString('o'); Executable = $executable; SHA256 = $hashes; PublishManifestHash = $manifestHash } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $latest.FullName '发布验证.json') -Encoding utf8

