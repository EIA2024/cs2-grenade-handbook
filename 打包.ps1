$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'scripts/发布配置.ps1')
if (-not (Test-Path -LiteralPath (Join-Path $releaseRoot '道具手册.exe'))) { throw '请先发布软件。' }
$evidence = (Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot '测试结果') -Directory | Sort-Object Name -Descending | Select-Object -First 1).FullName
if (-not $evidence -or -not (Test-Path -LiteralPath (Join-Path $evidence '发布验证.json'))) { throw '请先运行 验证.ps1 -Published。' }
$receipt = Get-Content -LiteralPath (Join-Path $evidence '发布验证.json') -Raw | ConvertFrom-Json
$log = Get-Content -LiteralPath (Join-Path $evidence '验证结果.txt') -Raw
if (-not $receipt.Published -or $log -notmatch 'RESULT: \d+ passed, 0 failed') { throw '发布包验收记录未通过。' }
foreach ($binary in @('道具手册.exe', '道具手册.dll', 'Handbook.Core.dll', 'Handbook.Storage.dll')) {
    if ((Get-FileHash -LiteralPath (Join-Path $releaseRoot $binary)).Hash -ne $receipt.SHA256.$binary) { throw '发布文件与验证记录不一致，请重新验证。' }
}
# Stage application/runtime files without collecting user data.
$staging = Join-Path $PSScriptRoot ('artifacts/打包暂存/' + [guid]::NewGuid().ToString('N'))
$packageRoot = Join-Path $staging '道具手册'
New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
if ($receipt.PublishManifestHash -ne (Get-FileHash -LiteralPath $PublishManifest).Hash) { throw '发布清单与验收记录不一致。' }
foreach ($file in @(Get-PublishedFiles)) {
    $destination = Join-Path $packageRoot $file.Path
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $releaseRoot $file.Path) -Destination $destination
}
foreach ($folder in @('图片包', '使用文档', '验证记录')) { New-Item -ItemType Directory -Path (Join-Path $packageRoot $folder) | Out-Null }
Copy-Item -LiteralPath (Join-Path $evidence '示例道具合集.zip') -Destination (Join-Path $packageRoot '图片包/示例道具合集.zip')
Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'docs') -File | Copy-Item -Destination (Join-Path $packageRoot '使用文档')
foreach ($file in @('验证结果.txt', '发布验证.json', '02-鼠标调整.png', '03-查看模式.png', '05-后台快捷浏览.png', '06-快捷键设置.png')) {
    Copy-Item -LiteralPath (Join-Path $evidence $file) -Destination (Join-Path $packageRoot '验证记录')
}
$quickStart = @"
道具手册 $ReleaseVersion · Windows x64

完整解压后双击“道具手册.exe”。不要只复制 EXE。无需额外安装 .NET 或 Game Bar。
首次体验：点击“图片包收件箱”，导入“示例道具合集.zip”。示例不是实际投掷教程。

在编辑器选好地图、阵营或道具，点击“进入查看模式”。自行点击前台应用一次后，即可保持焦点操作：
Ctrl+Alt+Q：打开 / 取消后台菜单（升级时沿用原选择快捷键）
鼠标侧键 1：上一项；鼠标侧键 2：下一项；鼠标中键：确认 / 进入
Ctrl+Alt+H：隐藏 / 显示图片（升级时沿用原显示快捷键）
以上五个动作均可在编辑器“快捷键”中重新录入。

菜单始终鼠标穿透、不激活窗口；每层“返回上一级”用于切换合集、地图和阵营。
高亮只是候选，确认才换图；取消保留原图。菜单关闭时上一项、下一项与确认不起作用。
按住只触发一次，松开后可再次触发。按键仍可能触发前台程序本身的动作。
隐藏不停止后台输入。返回编辑器、进入鼠标调整或退出软件会注销后台输入。
拖动、缩放和改变布局请使用编辑器或托盘“鼠标调整面板”。

双击“真实按键测试.cmd”，按使用文档完成真实输入验证；该窗口不模拟输入或访问游戏。
普通置顶不保证覆盖独占全屏。实际鼠标穿透、同键响应和游戏内显示仍需手动验证。

升级前从托盘退出旧版；将新版完整解压至原目录覆盖程序文件。
包内不含资料库、用户数据，不替换你的资料与设置；首次运行会迁移旧快捷键。
资料格式不变。更多说明见“使用文档”。
"@
Set-Content -LiteralPath (Join-Path $packageRoot '先读我.txt') -Value $quickStart -Encoding utf8
# ASCII-only launcher avoids console codepage dependence in a Chinese executable name.
Set-Content -LiteralPath (Join-Path $packageRoot '真实按键测试.cmd') -Encoding ascii -Value '@echo off', 'powershell -NoProfile -Command "$n = ([char]0x9053).ToString()+[char]0x5177+[char]0x624b+[char]0x518c; Start-Process -FilePath (Join-Path $env:GUIDE_TEST_ROOT ($n+''.exe'')) -ArgumentList ''--input-test''"'
$launcher = @('@echo off', 'set "GUIDE_TEST_ROOT=%~dp0"', (Get-Content -LiteralPath (Join-Path $packageRoot '真实按键测试.cmd'))[1])
Set-Content -LiteralPath (Join-Path $packageRoot '真实按键测试.cmd') -Value $launcher -Encoding ascii
$files = Get-ChildItem -LiteralPath $packageRoot -Recurse -File
$sums = foreach ($file in $files) { (Get-FileHash -LiteralPath $file.FullName).Hash + '  ' + [IO.Path]::GetRelativePath($packageRoot, $file.FullName) }
Set-Content -LiteralPath (Join-Path $packageRoot 'SHA256SUMS.txt') -Value $sums -Encoding utf8
$zip = Join-Path $PSScriptRoot ('artifacts/道具手册-v' + $ReleaseVersion + '-Windows-x64.zip')
Compress-Archive -LiteralPath $packageRoot -DestinationPath $zip -Force
$archive = [IO.Compression.ZipFile]::OpenRead($zip)
try {
    foreach ($file in (Get-ChildItem -LiteralPath $packageRoot -Recurse -File)) {
        $entryName = '道具手册/' + [IO.Path]::GetRelativePath($packageRoot, $file.FullName).Replace('\', '/')
        $entry = $archive.GetEntry($entryName)
        if ($null -eq $entry -or $entry.Length -ne $file.Length) { throw ('压缩包文件核对失败：' + $entryName) }
    }
    if ($archive.Entries.FullName -match '^道具手册/(资料库|用户数据)/') { throw '压缩包不应含用户资料。' }
} finally { $archive.Dispose() }
foreach ($folder in @('使用文档', '验证记录')) { Copy-Item -LiteralPath (Join-Path $packageRoot $folder) -Destination $releaseRoot -Recurse -Force }
foreach ($name in @('先读我.txt', '真实按键测试.cmd')) { Copy-Item -LiteralPath (Join-Path $packageRoot $name) -Destination $releaseRoot -Force }
$inbox = Join-Path $releaseRoot '图片包'
New-Item -ItemType Directory -Path $inbox -Force | Out-Null
if (-not (Test-Path -LiteralPath (Join-Path $inbox '示例道具合集.zip'))) { Copy-Item -LiteralPath (Join-Path $packageRoot '图片包/示例道具合集.zip') -Destination $inbox }
Write-Output $zip
Write-Output ('Bytes: ' + (Get-Item -LiteralPath $zip).Length)
Write-Output ('SHA256: ' + (Get-FileHash -LiteralPath $zip).Hash)


