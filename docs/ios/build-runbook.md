# iOS Build Runbook

This runbook defines the minimum commands to verify iOS-readiness for the LegionScript runtime work.

## 1) Host Preflight

Run:

```bash
tools/ios/ios-preflight.sh
```

Interpretation:

- `Overall: READY_FOR_SIMULATOR_VALIDATION`: host can run simulator-oriented compile/publish checks.
- `Overall: BLOCKED`: host is missing non-negotiable prerequisites (typically non-macOS, no Xcode, no signing toolchain).

## 2) Functional Regression Checks

Run LegionScript and ScriptRuntime suites:

```bash
dotnet test tests/ClassicUO.UnitTests/ClassicUO.UnitTests.csproj --filter "FullyQualifiedName~LegionScript"
dotnet test tests/ClassicUO.UnitTests/ClassicUO.UnitTests.csproj --filter "FullyQualifiedName~ScriptRuntime"
```

Expected: both suites pass before declaring readiness.

## 3) Baseline Release Build

```bash
dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj -c Release
```

Expected: build succeeds.

## 4) iOS Simulator Compile/Publish

```bash
dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj -c Release -r iossimulator-x64
dotnet publish src/ClassicUO.Client/ClassicUO.Client.csproj -c Release -r iossimulator-arm64 --self-contained false -o /tmp/tazuo-iossim-publish
```

Expected: both complete successfully and produce simulator-targeted outputs.

## 5) iOS Device Compile/Publish

```bash
dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj -c Release -r ios-arm64
dotnet publish src/ClassicUO.Client/ClassicUO.Client.csproj -c Release -r ios-arm64 --self-contained false -o /tmp/tazuo-ios-device-publish
```

Expected: compile/publish succeeds. Device install/deploy remains blocked until signing + macOS toolchain are available.

## 6) Verdict Rules

- `Ready (simulator)`:
  - regression suites pass
  - release build passes
  - simulator build/publish pass
- `Ready (device)`:
  - device build/publish pass
  - macOS/Xcode/signing prerequisites pass in preflight
- `Blocked (device)`:
  - any required signing/macOS/Xcode prerequisite is missing, even if cross-target publish succeeds

