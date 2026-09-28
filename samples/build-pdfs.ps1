# Renders samples/invoices/html/*.html to samples/invoices/*.pdf using headless Microsoft Edge.
$ErrorActionPreference = 'Stop'

$edge = @(
    "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
    "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $edge) { throw 'Microsoft Edge not found.' }

$root = Join-Path $PSScriptRoot 'invoices'
Get-ChildItem (Join-Path $root 'html') -Filter *.html | ForEach-Object {
    $pdf = Join-Path $root ($_.BaseName + '.pdf')
    $url = ([Uri]$_.FullName).AbsoluteUri
    Remove-Item $pdf -ErrorAction SilentlyContinue
    # Edge reports progress on stderr, so run it as a process instead of a native call.
    Start-Process $edge -ArgumentList '--headless', '--disable-gpu', '--no-pdf-header-footer', "`"--print-to-pdf=$pdf`"", $url -Wait -WindowStyle Hidden
    if (-not (Test-Path $pdf)) { throw "Failed to render $($_.Name)" }
    Write-Host "$($_.Name) -> $([IO.Path]::GetFileName($pdf))"
}
