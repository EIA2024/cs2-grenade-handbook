$WorkspaceRoot = Split-Path -Parent $PSScriptRoot
[xml]$projectXml = Get-Content -LiteralPath (Join-Path $WorkspaceRoot 'src/Handbook.Desktop/Handbook.Desktop.csproj')
$ReleaseVersion = [string]$projectXml.Project.PropertyGroup.Version
if ($ReleaseVersion -notmatch '^\d+\.\d+\.\d+$') { throw '无效发布版本。' }
$ReleaseRoot = Join-Path $WorkspaceRoot ('artifacts/道具手册-v' + $ReleaseVersion)
$PublishManifest = Join-Path $WorkspaceRoot ('artifacts/道具手册-v' + $ReleaseVersion + '.publish.json')
function Assert-PlainFile([string]$Path) {
    $item = Get-Item -LiteralPath $Path -Force -ErrorAction Stop
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw ('拒绝链接：' + $Path) }
    $parent = $item.Directory
    while ($null -ne $parent) {
        if ($parent.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw ('拒绝链接目录：' + $parent.FullName) }
        $parent = $parent.Parent
    }
}
function Get-PublishedFiles {
    Assert-PlainFile $PublishManifest
    $manifest = Get-Content -LiteralPath $PublishManifest -Raw | ConvertFrom-Json
    if ($manifest.Version -ne $ReleaseVersion) { throw '发布清单版本不一致。' }
    foreach ($file in $manifest.Files) {
        if ([IO.Path]::IsPathRooted($file.Path) -or $file.Path -match '(^|[\\/])\.\.([\\/]|$)|:' -or $file.Path -match '^(资料库|用户数据|图片包)([\\/]|$)') { throw '发布清单路径不合法。' }
        $path = [IO.Path]::GetFullPath((Join-Path $ReleaseRoot $file.Path))
        if (-not $path.StartsWith($ReleaseRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw '发布路径越界。' }
        Assert-PlainFile $path
        if ((Get-FileHash -LiteralPath $path).Hash -ne $file.SHA256) { throw ('发布文件已变化，请重新构建和验证：' + $file.Path) }
        $file
    }
}
