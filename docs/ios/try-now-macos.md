# Try Now (macOS)

Run this to execute the full iOS trial in one command:

```bash
bash tools/ios/ios-macos-trial.sh
```

The command performs:

- host preflight
- LegionScript + ScriptRuntime regression tests
- release build
- iOS simulator publish
- iOS device publish
- device-signing dry-run attempt (fails fast if external prerequisites are missing)

## Setup for Device Signing Attempt

```bash
cp tools/ios/ios-signing.env.template tools/ios/ios-signing.env
```

Fill required values in `tools/ios/ios-signing.env`:

- `APPLE_TEAM_ID`
- `IOS_CODESIGN_IDENTITY`
- `IOS_APP_BUNDLE_PATH`

If you only want simulator validation:

```bash
bash tools/ios/ios-macos-trial.sh --no-device-signing
```

## Expected Verdict Rules

- `Ready (simulator)`: command reaches simulator/device publish steps successfully.
- `Ready (device)`: signing dry-run passes with configured identity + app bundle.
- `Blocked (device)`: script exits with `BLOCKED_EXTERNAL` and explicit missing prerequisite.

