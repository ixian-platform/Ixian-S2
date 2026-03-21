#!/bin/sh -e
echo Rebuilding Ixian S2...

echo Checking .NET SDK Version
# Get the current active SDK version
DOTNET_VER=$(dotnet --version 2>/dev/null)

if [ -z "$DOTNET_VER" ]; then
    echo ".NET is not installed."
    exit 1
fi

# Extract the major version (everything before the first dot)
MAJOR_VER=$(echo "$DOTNET_VER" | cut -d. -f1)

if [ "$MAJOR_VER" -lt 10 ]; then
    echo "Found .NET $MAJOR_VER. Version is too old."
    exit 1
fi

echo Cleaning previous build
dotnet clean --configuration Release
echo Restoring packages
dotnet restore
echo Building Ixian S2
dotnet build --configuration Release -p WarningLevel=0
echo Done rebuilding Ixian S2