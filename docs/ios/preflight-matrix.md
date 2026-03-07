# iOS Preflight Matrix

Use `tools/ios/ios-preflight.sh` to validate whether a host can execute simulator/device build lanes.

## Matrix

| Check | Why it matters | Pass criteria |
|---|---|---|
| .NET SDK present | Required for build/test/publish commands | `dotnet --version` returns a value |
| macOS host | iOS simulator/device deployment requires Apple platform tooling | `uname -s` is `Darwin` |
| xcodebuild available | Required Apple compiler and SDK entrypoint | `xcodebuild` exists |
| xcode-select available | Required to point to active Xcode | `xcode-select` exists |
| dotnet iOS workload installed | Needed for `net*-ios` app projects | `dotnet workload list` contains `ios` |
| codesign available | Needed for signing app bundles | `codesign` exists |
| security CLI available | Needed for keychain/certificate automation | `security` exists |
| signing env configured | Required for device/App Store distribution | `APPLE_TEAM_ID` or `APP_STORE_CONNECT_API_KEY` present |

## Current Evidence (2026-03-07 UTC)

Command:

```bash
tools/ios/ios-preflight.sh
```

Result snapshot:

| Check | Result |
|---|---|
| .NET SDK present | PASS |
| macOS host | FAIL |
| xcodebuild available | FAIL |
| xcode-select available | FAIL |
| dotnet iOS workload installed | FAIL |
| codesign available | FAIL |
| security CLI available | FAIL |
| signing env configured | FAIL |

Overall: `BLOCKED` on this Linux host.

