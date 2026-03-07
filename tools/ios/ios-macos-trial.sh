#!/usr/bin/env bash

set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

RUN_TESTS=1
ATTEMPT_DEVICE_SIGNING=1

for arg in "$@"; do
  case "$arg" in
    --skip-tests) RUN_TESTS=0 ;;
    --no-device-signing) ATTEMPT_DEVICE_SIGNING=0 ;;
    *)
      echo "Unknown argument: $arg" >&2
      exit 64
      ;;
  esac
done

if [ -f "$ROOT_DIR/tools/ios/ios-signing.env" ]; then
  # shellcheck source=/dev/null
  source "$ROOT_DIR/tools/ios/ios-signing.env"
fi

fail_external() {
  echo
  echo "BLOCKED_EXTERNAL: $1" >&2
  exit 2
}

run() {
  echo
  echo "> $*"
  "$@"
}

if [ "$(uname -s)" != "Darwin" ]; then
  fail_external "This command must run on macOS (Darwin)."
fi

for cmd in dotnet xcodebuild xcode-select codesign security; do
  if ! command -v "$cmd" >/dev/null 2>&1; then
    fail_external "Missing required command '$cmd'. Install Xcode + CLT and ensure it is on PATH."
  fi
done

if ! dotnet workload list 2>/dev/null | grep -Eq '^ios[[:space:]]'; then
  fail_external "Missing dotnet iOS workload. Run: dotnet workload install ios"
fi

run bash "$ROOT_DIR/tools/ios/ios-preflight.sh"

if [ "$RUN_TESTS" -eq 1 ]; then
  run dotnet test "$ROOT_DIR/tests/ClassicUO.UnitTests/ClassicUO.UnitTests.csproj" --filter "FullyQualifiedName~LegionScript"
  run dotnet test "$ROOT_DIR/tests/ClassicUO.UnitTests/ClassicUO.UnitTests.csproj" --filter "FullyQualifiedName~ScriptRuntime"
fi

run dotnet build "$ROOT_DIR/src/ClassicUO.Client/ClassicUO.Client.csproj" -c Release
run dotnet publish "$ROOT_DIR/src/ClassicUO.Client/ClassicUO.Client.csproj" -c Release -r iossimulator-arm64 --self-contained false -o /tmp/tazuo-iossim-publish
run dotnet publish "$ROOT_DIR/src/ClassicUO.Client/ClassicUO.Client.csproj" -c Release -r ios-arm64 --self-contained false -o /tmp/tazuo-iosarm-publish

if [ "$ATTEMPT_DEVICE_SIGNING" -eq 1 ]; then
  if [ -z "${APPLE_TEAM_ID:-}" ]; then
    fail_external "APPLE_TEAM_ID is not set. Fill tools/ios/ios-signing.env from template."
  fi

  if [ -z "${IOS_CODESIGN_IDENTITY:-}" ]; then
    fail_external "IOS_CODESIGN_IDENTITY is not set. Fill tools/ios/ios-signing.env from template."
  fi

  if [ -z "${IOS_APP_BUNDLE_PATH:-}" ]; then
    fail_external "IOS_APP_BUNDLE_PATH is not set. Provide a signable .app bundle path from your iOS host build."
  fi

  if [ ! -d "$IOS_APP_BUNDLE_PATH" ]; then
    fail_external "IOS_APP_BUNDLE_PATH '$IOS_APP_BUNDLE_PATH' does not exist or is not a directory."
  fi

  if ! security find-identity -v -p codesigning | grep -Fq "$IOS_CODESIGN_IDENTITY"; then
    fail_external "Identity '$IOS_CODESIGN_IDENTITY' not found in keychain signing identities."
  fi

  run codesign --force --sign "$IOS_CODESIGN_IDENTITY" --dryrun "$IOS_APP_BUNDLE_PATH"
  echo
  echo "Device signing attempt: PASS (dryrun)"
else
  echo
  echo "Device signing attempt: SKIPPED (--no-device-signing)"
fi

echo
echo "iOS macOS trial workflow complete."

