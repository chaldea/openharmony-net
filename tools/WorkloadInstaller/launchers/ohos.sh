#!/bin/sh
# dotnet tool install creates a symlink to this script.
script=$0
while [ -L "$script" ]; do
    target=$(readlink "$script") || exit 1
    case "$target" in
        /*) script=$target ;;
        *) script=$(dirname "$script")/$target ;;
    esac
done
host=dotnet
if [ -n "$DOTNET_ROOT" ] && [ -x "$DOTNET_ROOT/dotnet" ]; then
    host=$DOTNET_ROOT/dotnet
fi
exec "$host" "$(dirname "$script")/WorkloadInstaller.dll" "$@"
