#Requires -Version 7.0
<#
.SYNOPSIS
  Builds the AppStart user manual as PDF (task 10.3): docs/manual/*.md -> HTML (build/ManualBuilder) -> PDF (headless Microsoft Edge).

.DESCRIPTION
  1. dotnet run build/ManualBuilder: index.md + chapters NN-*.md -> one standalone HTML for A4 printing
     (title page, table of contents, page break per chapter, images embedded, missing screenshots as placeholders).
  2. msedge --headless --print-to-pdf: HTML -> PDF, with a temporary Edge profile (does not touch the user's profile
     and works while Edge is open).
  3. Checks that the PDF exists and reports size and page count.

  Output (default): artifacts/manual/AppStart-Manual.html and artifacts/manual/AppStart-Manual.pdf.
  Needs Windows for the PDF step (Edge); with -SkipPdf only the HTML is built (any OS).

.EXAMPLE
  ./build/Build-Manual.ps1
.EXAMPLE
  ./build/Build-Manual.ps1 -Version 0.2.0 -OutputDir artifacts/release
.EXAMPLE
  ./build/Build-Manual.ps1 -EdgePath 'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'
#>
[CmdletBinding()]
param(
    # Shown on the title page. Default: <Version> from Directory.Build.props.
    [string] $Version,

    # Folder for AppStart-Manual.html and .pdf.
    [string] $OutputDir = 'artifacts/manual',

    # File name of the PDF (and of the HTML with .html).
    [string] $Name = 'AppStart-Manual',

    # msedge.exe; default: searched in Program Files (x86), Program Files and the App Paths registry key.
    [string] $EdgePath,

    # Build only the HTML.
    [switch] $SkipPdf,

    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$manualDir = Join-Path $root 'docs/manual'
$builder = Join-Path $PSScriptRoot 'ManualBuilder/ManualBuilder.csproj'
if (-not [IO.Path]::IsPathRooted($OutputDir)) { $OutputDir = Join-Path $root $OutputDir }
New-Item -ItemType Directory -Force $OutputDir | Out-Null
$OutputDir = (Resolve-Path $OutputDir).Path
$html = Join-Path $OutputDir "$Name.html"
$pdf = Join-Path $OutputDir "$Name.pdf"

if (-not $Version) {
    [xml] $props = Get-Content (Join-Path $root 'Directory.Build.props') -Raw
    $Version = @($props.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ })[0]
    if (-not $Version) { $Version = '0.0.0' }
}

function Find-Edge {
    $candidates = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Microsoft\Edge\Application\msedge.exe'),
        (Join-Path $env:ProgramFiles 'Microsoft\Edge\Application\msedge.exe')
    )
    foreach ($hive in 'HKLM:', 'HKCU:') {
        $key = "$hive\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\msedge.exe"
        $item = Get-Item $key -ErrorAction SilentlyContinue
        $value = if ($item) { $item.GetValue('') } else { $null }
        if ($value) { $candidates += ([string] $value).Trim('"') }
    }
    # During an Edge update msedge.exe may only exist in a versioned folder (Application\<ver>\ or EdgeCore\<ver>\).
    foreach ($base in (Join-Path ${env:ProgramFiles(x86)} 'Microsoft\Edge\Application'), (Join-Path ${env:ProgramFiles(x86)} 'Microsoft\EdgeCore')) {
        if (Test-Path $base) {
            $candidates += Get-ChildItem $base -Directory -ErrorAction SilentlyContinue |
                Where-Object { $_.Name -match '^\d+(\.\d+){3}$' } |
                Sort-Object { [version] $_.Name } -Descending |
                ForEach-Object { Join-Path $_.FullName 'msedge.exe' }
        }
    }
    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate -PathType Leaf)) { return $candidate }
    }
    throw 'Microsoft Edge (msedge.exe) not found. Specify the path with -EdgePath.'
}

function Get-PdfPageCount([string] $path) {
    # Chromium writes uncompressed page objects: count "/Type /Page" (not "/Pages").
    $text = [Text.Encoding]::Latin1.GetString([IO.File]::ReadAllBytes($path))
    return ([regex]::Matches($text, '/Type\s*/Page(?![a-zA-Z])')).Count
}

# 1. Markdown -> HTML
Write-Host "==> ManualBuilder ($Version)" -ForegroundColor Cyan
dotnet run --project $builder -c $Configuration -- --input $manualDir --output $html --version $Version
if ($LASTEXITCODE -ne 0) { throw "ManualBuilder fehlgeschlagen (Exit-Code $LASTEXITCODE)." }

if ($SkipPdf) {
    Write-Host "HTML: $html"
    return
}
if (-not $IsWindows) { throw 'Creating the PDF needs Windows with Microsoft Edge. HTML only: -SkipPdf.' }

# 2. HTML -> PDF with headless Edge and a throw-away profile
if (-not $EdgePath) { $EdgePath = Find-Edge }
Write-Host "==> Edge: $EdgePath" -ForegroundColor Cyan
$profileDir = Join-Path ([IO.Path]::GetTempPath()) ("appstart-manual-edge-" + [guid]::NewGuid().ToString('N'))
Remove-Item -LiteralPath $pdf -ErrorAction SilentlyContinue
$uri = ([Uri] $html).AbsoluteUri
$arguments = @(
    '--headless=new', '--disable-gpu', '--no-first-run', '--no-default-browser-check', '--disable-extensions',
    '--allow-file-access-from-files', "--user-data-dir=`"$profileDir`"",
    '--no-pdf-header-footer', '--print-to-pdf-no-header', "--print-to-pdf=`"$pdf`"",
    $uri
)
try {
    $process = Start-Process -FilePath $EdgePath -ArgumentList $arguments -PassThru -WindowStyle Hidden
    if (-not $process.WaitForExit(180000)) {
        $process.Kill($true)
        throw 'Edge did not create the PDF within 3 minutes.'
    }
    # Edge may hand the job to a child process; wait for the file to appear and stop growing.
    $deadline = (Get-Date).AddSeconds(60)
    $lastSize = -1
    while ((Get-Date) -lt $deadline) {
        if (Test-Path -LiteralPath $pdf) {
            $size = (Get-Item -LiteralPath $pdf).Length
            if ($size -gt 0 -and $size -eq $lastSize) { break }
            $lastSize = $size
        }
        Start-Sleep -Milliseconds 500
    }
}
finally {
    Remove-Item -LiteralPath $profileDir -Recurse -Force -ErrorAction SilentlyContinue
}

# 3. Check
if (-not (Test-Path -LiteralPath $pdf) -or (Get-Item -LiteralPath $pdf).Length -eq 0) {
    throw "PDF was not created: $pdf"
}
$pages = Get-PdfPageCount $pdf
$sizeKb = [math]::Round((Get-Item -LiteralPath $pdf).Length / 1KB)
if ($pages -lt 2) { throw "PDF has only $pages page(s): $pdf" }
Write-Host "PDF: $pdf ($pages Seiten, $sizeKb KB)" -ForegroundColor Green
