#!/bin/bash

CONFIG="Debug"
if [[ "${1,,}" == "release" ]]; then
    CONFIG="Release"
fi

echo "========================================"
echo " Building TazUO ($CONFIG)"
echo "========================================"
echo ""

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
