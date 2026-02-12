#!/bin/bash

CONFIG="Debug"
if [[ "${1,,}" == "release" ]]; then
    CONFIG="Release"
fi

echo "========================================"
echo " Building TazUO ($CONFIG)"
echo "========================================"
echo ""

# Initialize submodules if needed
if [ ! -f "external/FNA/FNA.Core.csproj" ] || [ ! -f "external/MP3Sharp/MP3Sharp/MP3Sharp.csproj" ] || [ ! -f "external/FileEmbed/FileEmbed/FileEmbed.csproj" ]; then
    echo "Initializing git submodules..."
    git submodule update --init --recursive
    echo ""
fi

dotnet build ClassicUO.sln -c "$CONFIG"

if [ $? -ne 0 ]; then
    echo ""
    echo "BUILD FAILED"
    exit 1
fi

echo ""
echo "========================================"
echo " Build succeeded!"
echo " Output: bin/$CONFIG/"
echo "========================================"
