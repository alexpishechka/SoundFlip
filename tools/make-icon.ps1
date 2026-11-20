# Regenerates src/app.ico from tools/IconGen.cs (uses the same glyph helpers as the app).
param([string]$PreviewDir)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
$tmp = Join-Path ([IO.Path]::GetTempPath()) ('soundflip-icongen-' + [guid]::NewGuid().ToString('N') + '.exe')
& $csc /nologo /codepage:65001 /out:$tmp /r:System.Drawing.dll /r:System.Windows.Forms.dll `
    (Join-Path $PSScriptRoot 'IconGen.cs') (Join-Path $root 'src\Theme.cs')
if ($LASTEXITCODE -ne 0) { throw "csc failed ($LASTEXITCODE)" }
try {
    if ($PreviewDir) { & $tmp (Join-Path $root 'src\app.ico') $PreviewDir } else { & $tmp (Join-Path $root 'src\app.ico') }
} finally { Remove-Item $tmp -ErrorAction SilentlyContinue }
