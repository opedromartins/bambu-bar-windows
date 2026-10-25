#!/bin/sh
set -eu
action="${1:-build}"
core=windows/BambuBar.Core/BambuBar.Core.csproj
desktop=windows/BambuBar.Desktop/BambuBar.Desktop.csproj
windows_tfm=net10.0-windows10.0.19041.0
tests=windows/BambuBar.Tests/BambuBar.Tests.csproj
export MSBUILDDISABLENODEREUSE=1
# CI runs with the host UID, which may have no passwd entry in the SDK image.
# Give .NET named mutexes a private, writable runtime directory for this invocation.
XDG_RUNTIME_DIR=$(mktemp -d /tmp/bambu-bar-runtime.XXXXXX)
XDG_DATA_HOME="$XDG_RUNTIME_DIR/data"
XDG_CONFIG_HOME="$XDG_RUNTIME_DIR/config"
XDG_CACHE_HOME="$XDG_RUNTIME_DIR/cache"
export XDG_RUNTIME_DIR XDG_DATA_HOME XDG_CONFIG_HOME XDG_CACHE_HOME
trap 'rm -rf "$XDG_RUNTIME_DIR"' EXIT
mkdir -p "$XDG_DATA_HOME" "$XDG_CONFIG_HOME" "$XDG_CACHE_HOME"
case "$action" in
  models)
    blender -b --factory-startup --python-exit-code 1 -P windows/scripts/export-models.py
    ;;
  restore)
    dotnet restore "$desktop"
    dotnet restore "$tests"
    ;;
  build)
    dotnet restore "$desktop" --locked-mode
    dotnet build "$desktop" -c Release -f "$windows_tfm" -r win-x64 --no-restore
    ;;
  test)
    dotnet restore "$tests" --locked-mode
    dotnet run --project "$tests" -c Release --no-restore
    ;;
  integration)
    dotnet restore "$tests" --locked-mode
    dotnet run --project "$tests" -c Release --no-restore -- --integration
    ;;
  preview)
    dotnet restore "$desktop" --locked-mode
    xvfb-run -a dotnet run --project "$desktop" -c Release -f net10.0 --no-restore -- --demo --smoke-test
    ;;
  package)
    dotnet restore "$desktop" --locked-mode
    rm -rf build/windows/app
    dotnet publish "$desktop" -c Release -f "$windows_tfm" -r win-x64 --self-contained true --no-restore -p:PublishTrimmed=false -o build/windows/app
    version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' windows/Directory.Build.props)
    mkdir -p build/windows
    rm -f "build/windows/Bambu-Bar-$version-windows-x64-portable.zip"
    makensis -DVERSION="$version" -DAPPDIR="$(pwd)/build/windows/app" -DOUTDIR="$(pwd)/build/windows" windows/installer/setup.nsi
    (cd build/windows/app && zip -q -r "../Bambu-Bar-$version-windows-x64-portable.zip" .)
    (cd build/windows && sha256sum "Bambu-Bar-$version-windows-x64-setup.exe" "Bambu-Bar-$version-windows-x64-portable.zip" > SHA256SUMS)
    ;;
  *) echo "Expected restore, build, test, integration, preview, models or package" >&2; exit 2 ;;
esac
