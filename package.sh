#!/bin/sh
# Builds the mod and creates the Thunderstore zip in dist/
set -e
cd "$(dirname "$0")"
dotnet build -c Release -nologo -v q
ver=$(python -c "import json;print(json.load(open('thunderstore/manifest.json'))['version_number'])")
rm -rf dist/pkg && mkdir -p dist/pkg/plugins
cp thunderstore/manifest.json thunderstore/README.md thunderstore/CHANGELOG.md thunderstore/icon.png dist/pkg/
cp bin/Release/KCMDeckBuilder.dll dist/pkg/plugins/
(cd dist/pkg && python -c "import shutil;shutil.make_archive('../KCM_Deck_Builder-$ver','zip','.')")
echo "dist/KCM_Deck_Builder-$ver.zip"
