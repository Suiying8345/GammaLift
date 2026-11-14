param([switch]$Native, [switch]$Desktop)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$out = Join-Path $root 'build'
if (-not (Test-Path $out)) { New-Item -ItemType Directory -Path $out | Out-Null }
$sources = @(Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object { $_.FullName })

foreach ($name in @('TestProfileShareCode', 'TestHotkeys', 'TestProfileOrder')) {
    $exe = Join-Path $out ($name + '.exe')
    $test = Join-Path $root ('tools\' + $name + '.cs')
    $testSources = $sources
    & $csc /nologo /codepage:65001 /warnaserror+ /target:exe `
        /main:GammaLift.$name /out:$exe `
        /r:System.dll /r:System.Core.dll /r:System.Drawing.dll `
        /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll $testSources $test
    if ($LASTEXITCODE -ne 0) { throw ('Compilation failed: ' + $name) }
    & $exe
    if ($LASTEXITCODE -ne 0) { throw ('Tests failed: ' + $name) }
}

if ($Native) {
    & (Join-Path $out 'TestHotkeys.exe') --native
    if ($LASTEXITCODE -ne 0) { throw 'Native hotkey test failed.' }
}

if ($Desktop) {
    $uiDir = Join-Path ([IO.Path]::GetTempPath()) ('GammaLift-DesktopUi-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $uiDir | Out-Null
    $uiExe = Join-Path $uiDir 'TestDesktopUi.exe'
    $uiSources = @($sources | Where-Object {
        [IO.Path]::GetFileName($_) -ne 'MonitorService.cs' -and
        [IO.Path]::GetFileName($_) -ne 'SystemIntegration.cs'
    })
    $uiArgs = @(
        '/nologo',
        '/codepage:65001',
        '/warnaserror+',
        '/target:exe',
        '/main:GammaLift.TestDesktopUi',
        ('/win32manifest:' + (Join-Path $root 'app.manifest')),
        ('/resource:' + (Join-Path $root 'app.ico') + ',GammaLift.ico'),
        ('/out:' + $uiExe),
        '/r:System.dll',
        '/r:System.Core.dll',
        '/r:System.Drawing.dll',
        '/r:System.Windows.Forms.dll',
        '/r:System.Web.Extensions.dll'
    )
    $uiArgs += $uiSources
    $uiArgs += (Join-Path $root 'tools\TestDesktopUi.cs')
    & $csc $uiArgs
    if ($LASTEXITCODE -ne 0) { throw 'Desktop UI test compilation failed.' }
    & $uiExe $uiDir
    if ($LASTEXITCODE -ne 0) { throw 'Desktop UI tests failed.' }
}
