param([switch]$Publish)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'scripts/发布配置.ps1')
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.tools/cli-home'
$sdk = Join-Path $PSScriptRoot '.tools/dotnet/dotnet.exe'
if (-not (Test-Path -LiteralPath $sdk)) { $sdk = 'dotnet' }
$project = Join-Path $PSScriptRoot 'src/Handbook.Desktop/Handbook.Desktop.csproj'
if ($Publish) {
    # A fresh SDK output prevents existing user files from becoming release contents.
    $fresh = Join-Path $PSScriptRoot ('artifacts/构建暂存/' + [guid]::NewGuid().ToString('N'))
    & $sdk publish $project -c Release -r win-x64 --self-contained true -o $fresh
    if ($LASTEXITCODE -ne 0) { throw '发布失败' }
    New-Item -ItemType Directory -Path $ReleaseRoot -Force | Out-Null
    $files = @(foreach ($file in (Get-ChildItem -LiteralPath $fresh -Recurse -File)) {
        $relative = [IO.Path]::GetRelativePath($fresh, $file.FullName)
        $destination = Join-Path $ReleaseRoot $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
        @{ Path = $relative; SHA256 = (Get-FileHash -LiteralPath $file.FullName).Hash }
    })
    @{ Version = $ReleaseVersion; Files = $files } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $PublishManifest -Encoding utf8
} else {
    & $sdk build $project -c Release
    if ($LASTEXITCODE -ne 0) { throw '构建失败' }
}
