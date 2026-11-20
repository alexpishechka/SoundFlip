# Build without Visual Studio or SDK: the C# compiler ships with every Windows (.NET Framework 4.x).
$ErrorActionPreference = 'Stop'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
New-Item -ItemType Directory -Force (Join-Path $PSScriptRoot 'dist') | Out-Null
New-Item -ItemType Directory -Force (Join-Path $PSScriptRoot 'obj') | Out-Null
Push-Location $PSScriptRoot
try {
    # Win32 resources (icon + manifest + en-US version info with publisher), so Task Manager shows "SoundFlip".
    $ver = [regex]::Match((Get-Content src\Program.cs -Raw), 'AssemblyVersion\("([0-9.]+)"\)').Groups[1].Value
    & $csc /nologo /out:obj\ResGen.exe tools\ResGen.cs | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "ResGen build failed" }
    & obj\ResGen.exe obj\app.res src\app.ico src\app.manifest $ver 'SoundFlip' 'alexpishechka' 'SoundFlip' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "ResGen failed" }

    & $csc /nologo /target:winexe /optimize+ /codepage:65001 `
        /out:dist\SoundFlip.exe `
        /win32res:obj\app.res /resource:src\app.ico,SoundFlip.app.ico `
        /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll `
        src\*.cs
    if ($LASTEXITCODE -ne 0) { throw "csc failed ($LASTEXITCODE)" }
    "OK: dist\SoundFlip.exe"
} finally { Pop-Location }
