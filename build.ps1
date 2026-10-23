# Rose builder - builds dist\Rose.exe
# Requires nothing but Windows: csc.exe from .NET Framework 4.x.

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src  = Join-Path $root 'src'
$dist = Join-Path $root 'dist'
$stub = Join-Path $src 'stub\StubProgram.cs'
$manifest = Join-Path $src 'app.manifest'
$out  = Join-Path $dist 'Rose.exe'

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) {
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path $csc)) {
    throw 'csc.exe not found - .NET Framework 4.x is required'
}

New-Item -ItemType Directory -Force -Path $dist | Out-Null

$sources = Get-ChildItem -LiteralPath $src -Filter '*.cs' -File | ForEach-Object { $_.FullName }
if ($sources.Count -eq 0) { throw 'no sources found' }

Write-Host "csc: $csc"
Write-Host "sources: $($sources.Count)"

$quotingArgs = @(
    '/nologo'
    '/target:winexe'
    '/platform:anycpu'
    '/optimize+'
    '/langversion:5'
    '/codepage:65001'
    '/warn:4'
    '/reference:System.dll'
    '/reference:System.Core.dll'
    '/reference:System.Drawing.dll'
    '/reference:System.Windows.Forms.dll'
    '/reference:System.Security.dll'
    "/resource:`"$stub`",StubTemplate.cs"
    "/win32manifest:`"$manifest`""
    "/out:`"$out`""
) + ($sources | ForEach-Object { "`"$_`"" })

& $csc @quotingArgs
if ($LASTEXITCODE -ne 0) { throw "compile failed (exit $LASTEXITCODE)" }

$info = Get-Item -LiteralPath $out
Write-Host ("built {0} ({1:N0} bytes)" -f $info.Name, $info.Length)

# sanity: the stub template must be embedded, or every build fails at runtime
$bytes = [System.IO.File]::ReadAllBytes($out)
$needle = [System.Text.Encoding]::UTF8.GetBytes('StubTemplate.cs')
$found = $false
for ($i = 0; $i -le ($bytes.Length - $needle.Length); $i++) {
    if ($bytes[$i] -eq $needle[0]) {
        $hit = $true
        for ($j = 1; $j -lt $needle.Length; $j++) {
            if ($bytes[$i + $j] -ne $needle[$j]) { $hit = $false; break }
        }
        if ($hit) { $found = $true; break }
    }
}

if (-not $found) {
    throw 'StubTemplate.cs is not embedded in the output'
}
Write-Host 'resource: StubTemplate.cs embedded'
Write-Host 'ok'
