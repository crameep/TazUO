#!/usr/bin/env bash

set -euo pipefail

ENFORCE=0
for arg in "$@"; do
  case "$arg" in
    --enforce) ENFORCE=1 ;;
  esac
done

ok() {
  [ "$1" = "PASS" ] && echo "PASS" || echo "FAIL"
}

has_cmd() {
  command -v "$1" >/dev/null 2>&1
}

DOTNET_VERSION="missing"
if has_cmd dotnet; then
  DOTNET_VERSION="$(dotnet --version 2>/dev/null || echo missing)"
fi

CHECK_DOTNET="FAIL"
if [ "$DOTNET_VERSION" != "missing" ]; then
  CHECK_DOTNET="PASS"
fi

CHECK_MACOS="FAIL"
if [ "$(uname -s)" = "Darwin" ]; then
  CHECK_MACOS="PASS"
fi

CHECK_XCODEBUILD="FAIL"
if has_cmd xcodebuild; then
  CHECK_XCODEBUILD="PASS"
fi

CHECK_XCODESELECT="FAIL"
if has_cmd xcode-select; then
  CHECK_XCODESELECT="PASS"
fi

CHECK_IOS_WORKLOAD="FAIL"
if has_cmd dotnet; then
  if dotnet workload list 2>/dev/null | grep -Eq '^ios[[:space:]]'; then
    CHECK_IOS_WORKLOAD="PASS"
  fi
fi

CHECK_CODESIGN="FAIL"
if has_cmd codesign; then
  CHECK_CODESIGN="PASS"
fi

CHECK_SECURITY="FAIL"
if has_cmd security; then
  CHECK_SECURITY="PASS"
fi

CHECK_SIGNING_ENV="FAIL"
if [ -n "${APPLE_TEAM_ID:-}" ] || [ -n "${APP_STORE_CONNECT_API_KEY:-}" ]; then
  CHECK_SIGNING_ENV="PASS"
fi

echo "# iOS Preflight Matrix"
echo
echo "Generated at (UTC): $(date -u +%Y-%m-%dT%H:%M:%SZ)"
echo "Host: $(uname -s) $(uname -m)"
echo
echo "| Check | Result | Notes |"
echo "|---|---|---|"
echo "| .NET SDK present | $(ok "$CHECK_DOTNET") | dotnet=${DOTNET_VERSION} |"
echo "| macOS host | $(ok "$CHECK_MACOS") | Required for simulator launch + device deployment |"
echo "| xcodebuild available | $(ok "$CHECK_XCODEBUILD") | Required for Apple SDK toolchain |"
echo "| xcode-select available | $(ok "$CHECK_XCODESELECT") | Required to pick active Xcode path |"
echo "| dotnet iOS workload installed | $(ok "$CHECK_IOS_WORKLOAD") | Needed for net*-ios app projects |"
echo "| codesign available | $(ok "$CHECK_CODESIGN") | Required for code signing |"
echo "| security CLI available | $(ok "$CHECK_SECURITY") | Required for keychain/cert handling in CI |"
echo "| signing env configured | $(ok "$CHECK_SIGNING_ENV") | APPLE_TEAM_ID or ASC key in environment |"

BLOCKED=0
if [ "$CHECK_DOTNET" != "PASS" ] || [ "$CHECK_MACOS" != "PASS" ] || [ "$CHECK_XCODEBUILD" != "PASS" ]; then
  BLOCKED=1
fi

echo
if [ "$BLOCKED" -eq 1 ]; then
  echo "Overall: BLOCKED"
  echo "Reason: Missing non-negotiable iOS host prerequisites."
else
  echo "Overall: READY_FOR_SIMULATOR_VALIDATION"
  if [ "$CHECK_SIGNING_ENV" = "PASS" ] && [ "$CHECK_CODESIGN" = "PASS" ]; then
    echo "Device path: potentially ready; run signed archive/export validation on macOS."
  else
    echo "Device path: blocked until signing requirements are configured."
  fi
fi

if [ "$ENFORCE" -eq 1 ] && [ "$BLOCKED" -eq 1 ]; then
  exit 1
fi

