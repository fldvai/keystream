#!/bin/bash
# Build KeystreamSetup.exe (one-click installer) from the Keystream tree.
#
# Canonical installer build path (see WORKFLOW.md section 9).
# build-setup.ps1 is the Windows-local alternative and must produce
# the same payload.zip layout as this script.
#
# What this script does:
#   1. Downloads the official Interception v1.0.1 release zip and
#      verifies its SHA-256 hash before using anything from it.
#   2. Publishes Keystream.App framework-dependent for win-x64 and
#      packs the publish output (contents only, not the folder) into
#      payload.zip, with the tree copy of interception.dll at the
#      payload root and the official install-interception.exe under
#      drivers/ (the layout DriverInstaller.FindInstallerExePath
#      looks for after extraction).
#   3. Publishes Keystream.Setup, which embeds payload.zip as its
#      only resource (see Keystream.Setup.csproj).
#   4. Copies the finished KeystreamSetup.exe to the work directory
#      root and deletes the transient build input (payload.zip)
#      from the tree, including on failure.
#
# Usage:
#   Keystream.Setup/build-setup.sh
#
# Environment:
#   KEYSTREAM_SETUP_WORKDIR  Optional work directory. When unset, a
#                            fresh directory under /tmp is created
#                            with mktemp. The work directory must
#                            never be inside the source tree and
#                            never be the reference snapshot
#                            directory. The deliverable
#                            KeystreamSetup.exe is left in the work
#                            directory root; the work directory
#                            itself is not deleted by this script.
#
# The installer itself is Windows-only and is not run by this script.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TREE="$(dirname "$SCRIPT_DIR")"
SETUP_DIR="$TREE/Keystream.Setup"

INTERCEPTION_URL="https://github.com/oblitum/Interception/releases/download/v1.0.1/Interception.zip"
INTERCEPTION_ZIP_SHA256="ad038963d6413055765128b0b931f6e765147c9916dba79e65d872b261f9af10"
INTERCEPTION_DLL_SHA256="ab88164c11b1b48488772d4c3bfaa4509d5b0ae9dbc5a691dc4f96f0260443c8"
INSTALLER_EXE_SHA256="e137863a79da797f08e7a137280ff2a123809044a888fd75ce9c973198915abe"

# Transient build input. It is created in the tree only while the
# Setup project is published, then removed by the exit trap below.
# It must never be committed; see .gitignore.
PAYLOAD_ZIP="$SETUP_DIR/payload.zip"

cleanup_transient() {
    rm -f "$PAYLOAD_ZIP"
}
trap cleanup_transient EXIT

verify_sha256() {
    local file="$1"
    local expected="$2"
    local label="$3"
    local actual
    actual="$(sha256sum "$file" | awk '{print $1}')"
    if [ "$actual" != "$expected" ]; then
        echo "SHA-256 mismatch for $label: expected $expected, got $actual" >&2
        exit 1
    fi
    echo "SHA-256 OK: $label"
}

for tool in curl unzip zip sha256sum; do
    command -v "$tool" >/dev/null 2>&1 || { echo "Required tool not found: $tool" >&2; exit 1; }
done

# Work directory: fresh mktemp directory under /tmp by default,
# overridable with KEYSTREAM_SETUP_WORKDIR. Resolve it to an
# absolute path, then refuse any location inside the source tree
# or inside the reference snapshot directory.
if [ -n "${KEYSTREAM_SETUP_WORKDIR:-}" ]; then
    WORK="$KEYSTREAM_SETUP_WORKDIR"
    mkdir -p "$WORK"
else
    WORK="$(mktemp -d /tmp/keystream-setup-build.XXXXXX)"
fi
WORK="$(cd "$WORK" && pwd)"
case "$WORK" in
    "$TREE"|"$TREE"/*)
        echo "Work directory must not be inside the source tree: $WORK" >&2
        exit 1
        ;;
esac
case "$WORK" in
    "$HOME/workspace/keystream-installer"|"$HOME/workspace/keystream-installer"/*)
        echo "Work directory must not be the reference snapshot directory: $WORK" >&2
        exit 1
        ;;
esac

export DOTNET_ROOT=~/dnx TMPDIR=~/tmpdir NUGET_PACKAGES=~/tmpdir/nugetpkgs DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

echo "Tree: $TREE"
echo "Work: $WORK"

# Remove stale transient inputs from any earlier interrupted run so
# the build below always starts from files this script produced.
cleanup_transient

echo "== 1. Download and verify the official Interception release =="
curl -fL -o "$WORK/Interception.zip" "$INTERCEPTION_URL"
verify_sha256 "$WORK/Interception.zip" "$INTERCEPTION_ZIP_SHA256" "Interception.zip"
rm -rf "$WORK/extracted"
unzip -q "$WORK/Interception.zip" -d "$WORK/extracted"
EXTRACTED_DLL="$WORK/extracted/Interception/library/x64/interception.dll"
EXTRACTED_INSTALLER="$WORK/extracted/Interception/command line installer/install-interception.exe"
test -f "$EXTRACTED_DLL" || { echo "interception.dll not found in Interception.zip" >&2; exit 1; }
test -f "$EXTRACTED_INSTALLER" || { echo "install-interception.exe not found in Interception.zip" >&2; exit 1; }
verify_sha256 "$EXTRACTED_DLL" "$INTERCEPTION_DLL_SHA256" "extracted interception.dll (x64)"
verify_sha256 "$EXTRACTED_INSTALLER" "$INSTALLER_EXE_SHA256" "extracted install-interception.exe"

echo "== 2. Publish the app (framework-dependent, lightweight) and pack the payload =="
rm -rf "$WORK/payload-src"
~/dnx/dotnet publish "$TREE/Keystream.App" -c Release -r win-x64 --self-contained false -o "$WORK/payload-src"
test -f "$WORK/payload-src/Keystream.exe" || { echo "Keystream.exe not found in payload" >&2; exit 1; }
~/dnx/dotnet publish "$TREE/Keystream.Uninstall" -c Release -r win-x64 --self-contained false -o "$WORK/payload-uninstall"
test -f "$WORK/payload-uninstall/Uninstall.exe" || { echo "Uninstall.exe not found in payload" >&2; exit 1; }
cp "$WORK/payload-uninstall/Uninstall.exe" "$WORK/payload-src/Uninstall.exe"
verify_sha256 "$TREE/Keystream.Windows/interception.dll" "$INTERCEPTION_DLL_SHA256" "tree Keystream.Windows/interception.dll"
cp "$TREE/Keystream.Windows/interception.dll" "$WORK/payload-src/interception.dll"
verify_sha256 "$WORK/payload-src/interception.dll" "$INTERCEPTION_DLL_SHA256" "payload interception.dll"
mkdir -p "$WORK/payload-src/drivers"
cp "$EXTRACTED_INSTALLER" "$WORK/payload-src/drivers/install-interception.exe"
verify_sha256 "$WORK/payload-src/drivers/install-interception.exe" "$INSTALLER_EXE_SHA256" "payload drivers/install-interception.exe"
(cd "$WORK/payload-src" && zip -rq "$PAYLOAD_ZIP" .)

echo "== 3. Publish the setup (single-file) =="
rm -rf "$WORK/publish-setup"
~/dnx/dotnet publish "$TREE/Keystream.Setup" -c Release -o "$WORK/publish-setup"
test -f "$WORK/publish-setup/KeystreamSetup.exe" || { echo "KeystreamSetup.exe not found in publish output" >&2; exit 1; }

echo "== 4. Copy the deliverable and clean the transient input =="
cp "$WORK/publish-setup/KeystreamSetup.exe" "$WORK/KeystreamSetup.exe"
cleanup_transient
trap - EXIT
echo "== DONE =="
ls -la "$WORK/KeystreamSetup.exe"
