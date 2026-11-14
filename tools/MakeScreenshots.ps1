param([string]$OutDir)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$build = Join-Path $root 'build'
if (-not (Test-Path $build)) { New-Item -ItemType Directory -Path $build | Out-Null }
if (-not $OutDir) { $OutDir = Join-Path $root 'docs\screenshots' }

$sources = @(Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object { $_.FullName })
$sources = @($sources | Where-Object {
    [IO.Path]::GetFileName($_) -ne 'MonitorService.cs' -and
    [IO.Path]::GetFileName($_) -ne 'SystemIntegration.cs'
})

$exe = Join-Path $build 'MakeScreenshots.exe'
$args = @(
    '/nologo',
    '/codepage:65001',
    '/target:exe',
    '/main:GammaLift.MakeScreenshots',
    ('/win32manifest:' + (Join-Path $root 'app.manifest')),
    ('/resource:' + (Join-Path $root 'app.ico') + ',GammaLift.ico'),
    ('/out:' + $exe),
    '/r:System.dll',
    '/r:System.Core.dll',
    '/r:System.Drawing.dll',
    '/r:System.Windows.Forms.dll',
    '/r:System.Web.Extensions.dll'
)
$args += $sources
$args += (Join-Path $root 'tools\MakeScreenshots.cs')

& $csc $args
if ($LASTEXITCODE -ne 0) { throw 'Screenshot tool compilation failed.' }
& $exe $OutDir
if ($LASTEXITCODE -ne 0) { throw 'Screenshot generation failed.' }
