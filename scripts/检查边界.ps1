$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$source = Get-ChildItem -LiteralPath (Join-Path $root 'src') -Recurse -Filter '*.cs' | Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }
$allowedInterop = @('src/Handbook.Desktop/Windows/NativeWindow.cs', 'src/Handbook.Desktop/Windows/RawInputCommandService.cs')
$forbidden = '\b(SetWindowsHookEx|GetAsyncKeyState|GetKeyState|SendInput|mouse_event|keybd_event|FindWindow|EnumWindows|GetForegroundWindow|SetForegroundWindow|OpenProcess|ReadProcessMemory|WriteProcessMemory|CreateRemoteThread|VirtualAllocEx)\s*\(|Process\.(GetProcesses|GetProcessById)|\b(HttpClient|WebClient|TcpClient|UdpClient)\b'
foreach ($file in $source) {
    $relative = [IO.Path]::GetRelativePath($root, $file.FullName).Replace('\','/')
    $code = Get-Content -LiteralPath $file.FullName -Raw
    if ($code -match $forbidden) { throw ('禁止的系统或网络调用：' + $relative) }
    if ($code -match '\[(DllImport|LibraryImport)' -and $relative -notin $allowedInterop) { throw ('原生接口不在Windows模块：' + $relative) }
    if ($relative -eq 'src/Handbook.Desktop/Presentation/ViewerWindow.cs' -and $code -match '\b(Activate|Focus)\s*\(') { throw '后台展示路径不能激活窗口。' }
    if ($relative.StartsWith('src/Handbook.Core/') -and $code -match '\b(File|Directory|FileStream)\s*\.|using System.Windows|DllImport') { throw 'Core 不应直接执行文件/窗口副作用。' }
}
$manifest = Get-Content -LiteralPath (Join-Path $root 'src/Handbook.Desktop/app.manifest') -Raw
if ($manifest -notmatch 'level="asInvoker" uiAccess="false"') { throw '应用权限声明发生变化。' }
Write-Output 'PASS boundary source guard (static checks; not a sandbox or VAC certification)'
