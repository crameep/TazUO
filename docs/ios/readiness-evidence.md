# iOS Readiness Evidence

Date (UTC): 2026-03-07
Branch: `auto/ios-readiness`

## Command Matrix

| Command | Result | Evidence summary |
|---|---|---|
| `bash tools/ios/ios-preflight.sh` | FAIL (host preflight) | Linux host, no `xcodebuild`, no `xcode-select`, no `codesign`, no iOS workload/signing env |
| `dotnet test ... --filter "FullyQualifiedName~LegionScript"` | PASS | Passed: 34, Failed: 0 |
| `dotnet test ... --filter "FullyQualifiedName~ScriptRuntime"` | PASS | Passed: 15, Failed: 0 |
| `dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj -c Release` | PASS | Release build succeeded |
| `dotnet build ... -r iossimulator-x64` | PASS | iOS simulator-targeted build succeeded |
| `dotnet publish ... -r iossimulator-arm64 --self-contained false` | PASS | Published to `/tmp/tazuo-iossim-publish` |
| `dotnet build ... -r ios-arm64` | PASS | Device-targeted build succeeded |
| `dotnet publish ... -r ios-arm64 --self-contained false` | PASS | Published to `/tmp/tazuo-iosarm-publish` |

## Final Verdict

- Simulator readiness: `Ready`
  - Rationale: regression tests pass + release build passes + simulator build/publish pass.
- Device readiness: `Blocked`
  - Non-fixable in this environment: host is Linux and lacks required Apple toolchain/signing components (`xcodebuild`, `xcode-select`, `codesign`, `security`, signing env).

