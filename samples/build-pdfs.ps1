# Renders samples/invoices/html/*.html to samples/invoices/*.pdf using headless Microsoft Edge.
# Usage: .\build-pdfs.ps1            # only HTML files that have no PDF yet
#        .\build-pdfs.ps1 -Name 07*  # re-render matching files
#        .\build-pdfs.ps1 -Force     # re-render everything (shifts the eval baseline: text layout may change)
param(
    [string] $Name = '*',
    [switch] $Force
)
$ErrorActionPreference = 'Stop'

$edge = @(
    "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
    "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $edge) { throw 'Microsoft Edge not found.' }

$root = Join-Path $PSScriptRoot 'invoices'
Get-ChildItem (Join-Path $root 'html') -Filter "$Name.html" | ForEach-Object {
    $pdf = Join-Path $root ($_.BaseName + '.pdf')
    if ((Test-Path $pdf) -and -not $Force -and $Name -eq '*') {
        Write-Host "$($_.Name): PDF exists, skipped (use -Name or -Force)"
        return
    }
    $url = ([Uri]$_.FullName).AbsoluteUri
    Remove-Item $pdf -ErrorAction SilentlyContinue
    # Edge reports progress on stderr, so run it as a process instead of a native call.
    Start-Process $edge -ArgumentList '--headless', '--disable-gpu', '--no-pdf-header-footer', "`"--print-to-pdf=$pdf`"", $url -Wait -WindowStyle Hidden
    if (-not (Test-Path $pdf)) { throw "Failed to render $($_.Name)" }
    Write-Host "$($_.Name) -> $([IO.Path]::GetFileName($pdf))"
}
