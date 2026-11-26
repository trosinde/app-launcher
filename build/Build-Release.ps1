<#
.SYNOPSIS
  Builds, tests, signs and packs AppStart as a per-machine Velopack MSI (tasks 7.1, 7.2).
  Same script locally and in the GitHub release workflow.

.DESCRIPTION
  1. dotnet restore / build / test (Release)
  2. publish AppStart.Service (main EXE, Velopack hooks), AppStart.Launcher (Native AOT) and
     AppStart.Setup (WPF, not single-file) and AppStart.TestApp (subfolder TestApp)
  3. vpk pack --msi --instLocation PerMachine --packTitle AppStart  ->  C:\Program Files\AppStart
  4. removes Velopack's per-user Setup.exe and Portable.zip: they install into a folder the user can
     write to (concept: "Nie das per-user-Setup von Velopack verwenden")
  5. renames the MSI to AppStart-<Version>.msi, builds the bootstrapper AppStart-Setup.exe with the MSI
     embedded (tasks 7.5/7.6), signs it, copies the manual as AppStart-Handbuch-<Version>.pdf and
     writes SHA256SUMS.txt

  Signing: with -PfxPath the certificate is imported into CurrentUser\My for the duration of the build
  and signtool signs by thumbprint (/sha1). The password never appears on a command line or in a log.
  The certificate is removed again at the end, also on failure. -CertificateThumbprint uses a
  certificate that is already installed (local builds). Without either, nothing is signed.

.EXAMPLE
  ./build/Build-Release.ps1 -Version 0.1.0
.EXAMPLE
  ./build/Build-Release.ps1 -Version 0.1.0 -PfxPath $env:RUNNER_TEMP\sign.pfx -PfxPassword $securePassword
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')]
    [string] $Version,

    [string] $PfxPath,
    [securestring] $PfxPassword,
    [ValidatePattern('^[0-9A-Fa-f]{40}$')]
    [string] $CertificateThumbprint,
    [string] $TimestampUrl = 'http://timestamp.digicert.com',
    [string] $OutputDir = 'artifacts/release',
    [switch] $SkipTests,
    # Local builds on machines without the MSVC linker (Visual Studio C++ tools): launcher as JIT single file.
    [switch] $NoAot
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$PSNativeCommandUseErrorActionPreference = $true

if (-not $IsWindows -and $PSVersionTable.PSEdition -eq 'Core') { throw 'Build-Release.ps1 needs Windows (vpk --msi, signtool).' }
if ($PfxPath -and $CertificateThumbprint) { throw 'Use either -PfxPath or -CertificateThumbprint, not both.' }

$root = Split-Path $PSScriptRoot -Parent
$OutputDir = if ([IO.Path]::IsPathRooted($OutputDir)) { $OutputDir } else { Join-Path $root $OutputDir }
$work = Join-Path $root 'artifacts/obj-release'
$pack = Join-Path $work 'pack'

function Invoke-Step([string] $title, [scriptblock] $action) {
    Write-Host "==> $title"
    & $action
    if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) { throw "$title failed (exit $LASTEXITCODE)." }
}

$importedThumbprint = $null
Push-Location $root
try {
    Remove-Item $work, $OutputDir -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force $pack, $OutputDir | Out-Null
    $common = @('-c', 'Release', "-p:Version=$Version", '--nologo')

    Invoke-Step 'Restore' { dotnet restore AppStart.slnx --nologo }
    Invoke-Step 'Build' { dotnet build AppStart.slnx @common --no-restore }
    if (-not $SkipTests) {
        Invoke-Step 'Test' { dotnet test --solution AppStart.slnx -c Release --no-build }
    }

    Invoke-Step 'Publish AppStart.Service' {
        dotnet publish src/AppStart.Service @common -r win-x64 --self-contained -o $pack
    }
    Invoke-Step 'Publish AppStart.Launcher' {
        if ($NoAot) {
            Write-Warning 'Launcher without Native AOT (-NoAot): only for local tests, not for releases.'
            dotnet publish src/AppStart.Launcher @common -r win-x64 --self-contained -p:PublishAot=false -p:PublishSingleFile=true -o $pack
        } else {
            dotnet publish src/AppStart.Launcher @common -r win-x64 -o $pack
        }
    }
    Invoke-Step 'Publish AppStart.Setup' {
        # Not single-file: a self-extracting WPF EXE would unpack native libraries to %TEMP% while running
        # elevated (design D6). It shares the self-contained runtime folder with the service.
        dotnet publish src/AppStart.Setup @common -r win-x64 --self-contained -o $pack
    }
    Invoke-Step 'Publish AppStart.TestApp' {
        dotnet publish src/AppStart.TestApp @common -r win-x64 --self-contained -p:PublishSingleFile=true `
            -p:IncludeNativeLibrariesForSelfExtract=true -o (Join-Path $pack 'TestApp')
        # Helper program of the TestApp (add-allowed-child-processes 6.1), next to AppStart.TestApp.exe.
        dotnet publish src/AppStart.TestHelper @common -r win-x64 --self-contained -p:PublishSingleFile=true `
            -p:PublishTrimmed=true -o (Join-Path $pack 'TestApp')
    }
    Invoke-Step 'Manual (PDF)' { & (Join-Path $PSScriptRoot 'Build-Manual.ps1') -Version $Version -OutputDir (Join-Path $work 'manual') }
    Copy-Item (Join-Path $work 'manual/AppStart-Handbuch.pdf') (Join-Path $pack 'AppStart-Handbuch.pdf')
    Get-ChildItem $pack -Recurse -Filter '*.pdb' | Remove-Item

    # --- signing setup (no secret on any command line)
    $signArgs = @()
    if ($PfxPath) {
        if (-not $PfxPassword) { throw '-PfxPassword is required with -PfxPath.' }
        $cert = Import-PfxCertificate -FilePath $PfxPath -CertStoreLocation Cert:\CurrentUser\My -Password $PfxPassword
        $importedThumbprint = $cert.Thumbprint
        $CertificateThumbprint = $cert.Thumbprint
        Write-Host "Signing certificate: $($cert.Subject), valid until $($cert.NotAfter.ToString('yyyy-MM-dd'))"
    }
    if ($CertificateThumbprint) {
        $signArgs = @('--signParams', "/sha1 $CertificateThumbprint /s My /fd sha256 /tr $TimestampUrl /td sha256")
    } else {
        Write-Warning 'No certificate: the release is NOT signed (only acceptable for prereleases and local tests).'
    }

    Invoke-Step 'Restore tools (vpk)' { dotnet tool restore }
    Invoke-Step 'vpk pack --msi (PerMachine)' {
        dotnet vpk pack --packId AppStart --packVersion $Version --packDir $pack --mainExe AppStart.Service.exe `
            --packTitle AppStart --packAuthors AppStart --icon (Join-Path $root 'assets/icon/AppStart.ico') --msi --instLocation PerMachine --shortcuts '' --outputDir $OutputDir @signArgs
    }

    # --- release content: only the per-machine MSI and the update packages
    Get-ChildItem $OutputDir -Filter '*-Setup.exe' | Remove-Item
    Get-ChildItem $OutputDir -Filter '*-Portable.zip' | Remove-Item
    $msi = Get-ChildItem $OutputDir -Filter '*.msi' | Select-Object -First 1
    if (-not $msi) { throw 'vpk did not produce an MSI.' }
    $msiName = "AppStart-$Version.msi"
    Rename-Item $msi.FullName $msiName

    if ($CertificateThumbprint) {
        # vpk signs copies in its own staging folder, not $pack: check what actually ships, i.e. the MSI
        # and every EXE inside the full package (lib/app). Own EXEs must carry our certificate.
        $nupkg = Get-ChildItem $OutputDir -Filter '*-full.nupkg' | Select-Object -First 1
        if (-not $nupkg) { throw 'vpk did not produce a full package.' }
        $extract = Join-Path $work 'verify'
        New-Item -ItemType Directory -Force $extract | Out-Null
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [System.IO.Compression.ZipFile]::ExtractToDirectory($nupkg.FullName, $extract)

        $shipped = @(Get-Item (Join-Path $OutputDir $msiName)) + @(Get-ChildItem (Join-Path $extract 'lib/app') -Recurse -Filter '*.exe')
        $ownExes = 0
        foreach ($file in $shipped) {
            $sig = Get-AuthenticodeSignature $file.FullName
            $own = $file.Extension -eq '.msi' -or $file.Name -like 'AppStart.*.exe'
            if ($sig.Status -ne 'Valid') { throw "Signature check failed for $($file.Name) ($($sig.Status))." }
            if ($own -and $sig.SignerCertificate.Thumbprint -ne $CertificateThumbprint) {
                throw "$($file.Name) is signed by $($sig.SignerCertificate.Subject), not by the release certificate."
            }
            if ($own -and $file.Extension -eq '.exe') { $ownExes++ }
        }
        foreach ($required in 'AppStart.Service.exe', 'AppStart.Launcher.exe', 'AppStart.Setup.exe') {
            if (-not ($shipped | Where-Object Name -eq $required)) { throw "$required missing in the package (lib/app)." }
        }
        Write-Host "Signatures verified: MSI and $($shipped.Count - 1) EXE in the package ($ownExes own)."
    }

    # --- 7.5/7.6: bootstrapper AppStart-Setup.exe with the (signed) MSI embedded; built after vpk pack
    Invoke-Step 'Publish AppStart-Setup.exe (bootstrapper)' {
        $msiPath = Join-Path $OutputDir $msiName
        $bootOut = Join-Path $work 'bootstrapper'
        if ($NoAot) {
            Write-Warning 'Bootstrapper without Native AOT (-NoAot): only for local tests.'
            dotnet publish src/AppStart.Bootstrapper @common -r win-x64 --self-contained -p:PublishAot=false -p:PublishSingleFile=true "-p:AppStartMsi=$msiPath" -o $bootOut
        } else {
            dotnet publish src/AppStart.Bootstrapper @common -r win-x64 "-p:AppStartMsi=$msiPath" -o $bootOut
        }
        Copy-Item (Join-Path $bootOut 'AppStart-Setup.exe') (Join-Path $OutputDir 'AppStart-Setup.exe')
    }
    if ($CertificateThumbprint) {
        $signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
            Sort-Object FullName -Descending | Select-Object -First 1
        if (-not $signtool) { throw 'signtool.exe not found (Windows SDK).' }
        $boot = Join-Path $OutputDir 'AppStart-Setup.exe'
        Invoke-Step 'Sign AppStart-Setup.exe' { & $signtool.FullName sign /sha1 $CertificateThumbprint /s My /fd sha256 /tr $TimestampUrl /td sha256 $boot }
        $sig = Get-AuthenticodeSignature $boot
        if ($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Thumbprint -ne $CertificateThumbprint) { throw 'AppStart-Setup.exe signature check failed.' }
        Write-Host 'Signature verified: AppStart-Setup.exe'
    }

    # The manual also as a release asset, readable without installing AppStart (same file as in the package).
    Copy-Item (Join-Path $work 'manual/AppStart-Handbuch.pdf') (Join-Path $OutputDir "AppStart-Handbuch-$Version.pdf")

    $sums = Get-ChildItem $OutputDir -File | Sort-Object Name | ForEach-Object {
        "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
    }
    Set-Content (Join-Path $OutputDir 'SHA256SUMS.txt') $sums -Encoding ascii
    Write-Host "==> Done: $OutputDir"
    Get-ChildItem $OutputDir | ForEach-Object { Write-Host ("    {0,-45} {1,12:N0} bytes" -f $_.Name, $_.Length) }
}
finally {
    if ($importedThumbprint) {
        Remove-Item "Cert:\CurrentUser\My\$importedThumbprint" -DeleteKey -ErrorAction SilentlyContinue
        Write-Host 'Signing certificate removed from CurrentUser\My.'
    }
    Pop-Location
}
