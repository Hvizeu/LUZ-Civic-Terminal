#!/bin/sh
set -eu
cd -- "$(dirname -- "$0")"
bundle="$PWD/LUZ Civic Terminal.app"
if [ ! -d "$bundle/Contents/MacOS" ]; then
  printf '%s\n' 'Keep this helper beside LUZ Civic Terminal.app.'
  exit 1
fi
for binary in "$bundle/Contents/MacOS/"*; do
  case "$binary" in
    *.dylib|*/createdump|*/'LUZ Civic Terminal')
      chmod u+x "$binary"
      /usr/bin/codesign --force --sign - "$binary"
      ;;
  esac
done
/usr/bin/codesign --force --sign - "$bundle"
/usr/bin/codesign --verify --deep --strict "$bundle"
printf '%s\n' 'Local signing complete. Open LUZ Civic Terminal.app when ready.'
printf '%s\n' 'This preview is not notarized. macOS may require Open Anyway in Privacy & Security.'
