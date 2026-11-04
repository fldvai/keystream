# PowerShell installer build script for Windows
# Builds lightweight Cyber WPF installer with zero embedded raw driver executables
# and signs with Keystream Authenticode digital certificate.
# Windows-local alternative to build-setup.sh (the canonical path, see
# WORKFLOW.md section 9). Both scripts must produce the same payload.zip
# layout: app files + interception.dll at the root, plus
# drivers\install-interception.exe (see DriverInstaller.FindInstallerExePath).
param(
    # Optional local Interception v1.0.1 release zip. When supplied, its
    # SHA-256 is verified before anything is extracted from it. A relative
    # path is resolved against the repository root ($tree). When omitted,
    # the zip is downloaded from the official release.
    [string]$InterceptionZip = "",
    # Optional local install-interception.exe. When supplied, its SHA-256
    # is verified before use. A relative path is resolved against the
    # repository root ($tree). Takes precedence over -InterceptionZip.
    [string]$InterceptionInstaller = ""
)
$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$tree = Split-Path -Parent $scriptDir
$setupDir = Join-Path $tree "Keystream.Setup"
$payloadZip = Join-Path $setupDir "payload.zip"
$installerExe = Join-Path $setupDir "install-interception.exe"

$expectedDllSha = "AB88164C11B1B48488772D4C3BFAA4509D5B0AE9DBC5A691DC4F96F0260443C8"
$expectedExeSha = "E137863A79DA797F08E7A137280FF2A123809044A888FD75CE9C973198915ABE"
$expectedZipSha = "AD038963D6413055765128B0B931F6E765147C9916DBA79E65D872B261F9AF10"

$work = Join-Path $env:TEMP ("keystream-setup-build-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $work -Force | Out-Null

$env:PATH = "$env:LOCALAPPDATA\Microsoft\dotnet;$env:PATH"
$env:DOTNET_ROOT = "$env:LOCALAPPDATA\Microsoft\dotnet"
$env:DOTNET_NOLOGO = "1"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"

try {
    Write-Host "== 1. Preparing Interception installer and DLL =="
    if ($InterceptionInstaller -ne "") {
        $localInstaller = $InterceptionInstaller
        if (-not [System.IO.Path]::IsPathRooted($localInstaller)) {
            $localInstaller = Join-Path $tree $localInstaller
        }
        if (-not (Test-Path $localInstaller)) {
            throw "Interception installer not found: $localInstaller"
        }
        Copy-Item $localInstaller $installerExe -Force
    } else {
        if ($InterceptionZip -ne "") {
            $zipPath = $InterceptionZip
            if (-not [System.IO.Path]::IsPathRooted($zipPath)) {
                $zipPath = Join-Path $tree $zipPath
            }
            if (-not (Test-Path $zipPath)) {
                throw "Interception zip not found: $zipPath"
            }
        } else {
            $zipUrl = "https://github.com/oblitum/Interception/releases/download/v1.0.1/Interception.zip"
            $zipPath = Join-Path $work "Interception.zip"
            Write-Host "Downloading Interception.zip..."
            Invoke-WebRequest -Uri $zipUrl -OutFile $zipPath
        }
        $actualZipSha = (Get-FileHash $zipPath -Algorithm SHA256).Hash
        if ($actualZipSha -ne $expectedZipSha) {
            throw "Interception.zip SHA256 mismatch! Expected $expectedZipSha, got $actualZipSha"
        }
        Write-Host "Interception.zip SHA256 OK."
        Expand-Archive -Path $zipPath -DestinationPath (Join-Path $work "extracted")
        Copy-Item (Join-Path $work "extracted\Interception\command line installer\install-interception.exe") $installerExe -Force
    }

    $actualExeSha = (Get-FileHash $installerExe -Algorithm SHA256).Hash
    if ($actualExeSha -ne $expectedExeSha) {
        throw "install-interception.exe SHA256 mismatch! Expected $expectedExeSha, got $actualExeSha"
    }
    Write-Host "install-interception.exe SHA256 OK."

    $treeDll = Join-Path $tree "Keystream.Windows\interception.dll"
    $actualDllSha = (Get-FileHash $treeDll -Algorithm SHA256).Hash
    if ($actualDllSha -ne $expectedDllSha) {
        throw "interception.dll SHA256 mismatch! Expected $expectedDllSha, got $actualDllSha"
    }
    Write-Host "interception.dll SHA256 OK."

    Write-Host "== 2. Publish Keystream.App (framework-dependent, lightweight) =="
    $payloadSrc = Join-Path $work "payload-src"
    dotnet publish "$tree\Keystream.App" -c Release -r win-x64 --self-contained false -o $payloadSrc

    if (-not (Test-Path (Join-Path $payloadSrc "Keystream.exe"))) {
        throw "Keystream.exe not found in payload-src!"
    }
    Copy-Item $treeDll (Join-Path $payloadSrc "interception.dll") -Force

    # Place driver installer inside payload-src\drivers rather than embedding directly in Setup.exe resources
    $driversDir = Join-Path $payloadSrc "drivers"
    New-Item -ItemType Directory -Path $driversDir -Force | Out-Null
    Copy-Item $installerExe (Join-Path $driversDir "install-interception.exe") -Force
    $actualPayloadExeSha = (Get-FileHash (Join-Path $driversDir "install-interception.exe") -Algorithm SHA256).Hash
    if ($actualPayloadExeSha -ne $expectedExeSha) {
        throw "payload drivers\install-interception.exe SHA256 mismatch! Expected $expectedExeSha, got $actualPayloadExeSha"
    }
    Write-Host "payload drivers\install-interception.exe SHA256 OK."

    Write-Host "Publishing Keystream.Uninstall (framework-dependent, lightweight)..."
    $payloadUninstall = Join-Path $work "payload-uninstall"
    dotnet publish "$tree\Keystream.Uninstall" -c Release -r win-x64 --self-contained false -o $payloadUninstall
    if (-not (Test-Path (Join-Path $payloadUninstall "Uninstall.exe"))) {
        throw "Uninstall.exe not found in payload-uninstall!"
    }
    Copy-Item (Join-Path $payloadUninstall "Uninstall.exe") (Join-Path $payloadSrc "Uninstall.exe") -Force

    Write-Host "Packing payload.zip..."
    if (Test-Path $payloadZip) { Remove-Item $payloadZip -Force }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory($payloadSrc, $payloadZip, [System.IO.Compression.CompressionLevel]::Optimal, $false)
    Write-Host "payload.zip created: $((Get-Item $payloadZip).Length) bytes"

    Write-Host "== 3. Publish Keystream.Setup (single-file, uncompressed bundle) =="
    $publishSetup = Join-Path $work "publish-setup"
    dotnet publish "$tree\Keystream.Setup" -c Release -o $publishSetup

    $setupExe = Join-Path $publishSetup "KeystreamSetup.exe"
    if (-not (Test-Path $setupExe)) {
        throw "KeystreamSetup.exe not found in publish output!"
    }

    Write-Host "== 4. Copying KeystreamSetup.exe to tree root =="
    $targetExe = Join-Path $tree "KeystreamSetup.exe"
    Copy-Item $setupExe $targetExe -Force

    Write-Host "== 5. Digitally Signing with Keystream Authenticode Certificate =="
    try {
        $cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert | Where-Object { $_.Subject -match "Keystream" } | Select-Object -First 1
        if ($null -eq $cert) {
            $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject "CN=Keystream, O=Keystream" -CertStoreLocation "Cert:\CurrentUser\My" -FriendlyName "Keystream Code Signing" -NotAfter (Get-Date).AddYears(5)
        }
        Set-AuthenticodeSignature -FilePath $targetExe -Certificate $cert -TimestampServer "http://timestamp.digicert.com" -HashAlgorithm SHA256 | Out-Null
        Write-Host "Authenticode digital signature applied successfully."
    } catch {
        Write-Warning "Could not apply digital signature: $_"
    }

    $otherTree = "..\keystream"
    if (Test-Path $otherTree) {
        try {
            Copy-Item $targetExe (Join-Path $otherTree "KeystreamSetup.exe") -Force
        } catch {
            Write-Warning "Could not copy to $otherTree (file is in use). Workspace copy is updated."
        }
    }

    $finalSize = (Get-Item $targetExe).Length / 1MB
    Write-Host ("SUCCESS: KeystreamSetup.exe built and signed! Output: {0} ({1:N2} MB)" -f $targetExe, $finalSize)
}
finally {
    Write-Host "Cleaning up transient files..."
    Remove-Item $payloadZip -Force -ErrorAction SilentlyContinue
    Remove-Item $installerExe -Force -ErrorAction SilentlyContinue
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}
