# GammaLift 构建脚本 — 使用 Windows 自带的 .NET Framework C# 编译器, 无需安装任何 SDK
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$out = Join-Path $root "build"
if (-not (Test-Path $out)) { New-Item -ItemType Directory -Path $out | Out-Null }

$sources = @(Get-ChildItem (Join-Path $root "src") -Filter *.cs | ForEach-Object { $_.FullName })
$cscArgs = @(
    '/nologo',
    '/codepage:65001',
    '/target:winexe',
    '/platform:anycpu',
    '/optimize+',
    ('/win32manifest:' + (Join-Path $root 'app.manifest')),
    ('/win32icon:' + (Join-Path $root 'app.ico')),
    ('/resource:' + (Join-Path $root 'app.ico') + ',GammaLift.ico'),
    ('/out:' + (Join-Path $out 'GammaLift.exe')),
    '/r:System.dll',
    '/r:System.Core.dll',
    '/r:System.Drawing.dll',
    '/r:System.Windows.Forms.dll',
    '/r:System.Web.Extensions.dll'
)
$cscArgs += $sources

& $csc $cscArgs

if ($LASTEXITCODE -eq 0) {
    Write-Host "构建成功: $out\GammaLift.exe"
} else {
    Write-Host "构建失败 (exit $LASTEXITCODE)"
    exit $LASTEXITCODE
}
