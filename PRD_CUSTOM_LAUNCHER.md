# PRD: Custom TazUO Launcher

## Overview

Fork the TazUO Launcher (`PlayTazUO/TUO-Launcher`) and modify it to pull client builds from `crameep/TazUO` instead of the upstream repo. Set up automated CI/CD to build and publish platform-specific release zips. Add branch-based install so feature branches can be tested on any machine via the launcher.

## Decisions

- **Launcher self-update**: Yes - launcher checks `crameep/TUO-Launcher` for its own updates
- **NET472 legacy channel**: Remove entirely
- **Bleeding-edge source**: `main` branch - every push auto-publishes a dev build
- **Stable releases**: Tag `v*` on main
- **macOS code signing**: Skip for now, can add later
- **Branding**: Minimal - add "Crameep's Build" subtitle under logo, update window title

## Goals

1. Launcher points at `crameep/TazUO` for client downloads
2. Launcher self-updates from `crameep/TUO-Launcher` releases
3. GitHub Actions builds platform zips (Windows x64, macOS ARM64, macOS x64, Linux x64) automatically
4. Three channels: Stable (tagged), Bleeding Edge (main), and Feature Branch (any branch)
5. Branch selector in launcher UI lets you install builds from any active feature branch

## Non-Goals

- Full rebrand of the launcher
- .NET 4.7.2 legacy channel support (removed)
- macOS code signing (deferred)

---

## Part 1: Launcher Fork - Branding

### File: `Windows/MainWindow.axaml`

**Window title** (line 1):
```xml
Title="TazUO - Crameep's Build"
```

**Add subtitle below the logo** (after the logo Rectangle element):
```xml
<TextBlock Canvas.Left="300" Canvas.Top="245" Width="200" TextAlignment="Center"
           Foreground="#FFFFFF" FontSize="12" Opacity="0.75">
    Crameep's Build
</TextBlock>
```

The ProfileSelector ComboBox starts at Canvas.Top="255", so this fits in the 5px gap. May need to nudge the ProfileSelector down by ~15px to give breathing room.

---

## Part 2: Launcher Fork - URL Changes

### File: `Constants.cs`

```csharp
// Client downloads - point at crameep/TazUO
public const string MAIN_CHANNEL_RELEASE_URL =
    "https://api.github.com/repos/crameep/TazUO/releases/latest";
public const string DEV_CHANNEL_RELEASE_URL =
    "https://api.github.com/repos/crameep/TazUO/releases/tags/TazUO-BleedingEdge";

// Launcher self-update - point at crameep/TUO-Launcher
public const string LAUNCHER_RELEASE_URL =
    "https://api.github.com/repos/crameep/TUO-Launcher/releases/latest";

// Web links
public const string GITHUB_URL = "https://github.com/crameep/TazUO";

// Branch builds API (new)
public const string BRANCH_BUILDS_API_URL =
    "https://api.github.com/repos/crameep/TazUO/releases";
```

### Remove NET472 Channel

- Remove `NET472_CHANNEL_RELEASE_URL` constant
- Remove NET472 option from the `ReleaseChannel` enum / UI dropdown
- Remove `TryGetReleaseData(ReleaseChannel.NET472)` call in `UpdateHelper.cs`

---

## Part 3: Launcher Fork - Branch Selector Feature

### Concept

Replace the old NET472 channel slot with a "Feature Branch" channel. When selected, the launcher shows a dropdown of available branches that have builds. The user picks a branch and the launcher downloads that branch's latest build.

### How It Works

**CI side** (on `crameep/TazUO`):
- A GitHub Actions workflow triggers on push to any branch matching a pattern (e.g., `autoloot-*`, `feature-*`, or all non-main branches)
- Creates/updates a GitHub Release tagged `branch-<branchname>` (e.g., `branch-autoloot-loot-profiles`)
- Release title includes the branch name for display in the launcher
- Old branch releases are cleaned up when the branch is deleted (via a `delete` event workflow)

**Launcher side**:
- When "Feature Branch" channel is selected, fetch all releases from `BRANCH_BUILDS_API_URL`
- Filter to releases where tag starts with `branch-`
- Display branch names in a dropdown
- Download/install works the same as other channels - just targets the selected branch release
- Store last selected branch in `launcherdata.json`

### UI Changes

In `MainWindow.axaml`:
- Replace the NET472 channel option with "Feature Branch" in the channel selector
- When "Feature Branch" is selected, show a second dropdown listing available branches
- Branch dropdown fetches from GitHub API on selection (with caching)

### Data Flow

```
User selects "Feature Branch" channel
  → Launcher fetches: GET /repos/crameep/TazUO/releases
  → Filters releases with tag prefix "branch-"
  → Populates branch dropdown with release names
  → User picks "autoloot-loot-profiles"
  → Launcher downloads from that release's assets (same platform zip logic)
  → Installs to ClientPath (same extraction logic)
  → v.txt contains: "branch-autoloot-loot-profiles.YYYYMMDD.SHA"
```

### Configuration

**`launcherdata.json`** additions:
```json
{
  "DownloadChannel": 4,
  "SelectedBranch": "autoloot-loot-profiles"
}
```

---

## Part 4: CI/CD on `crameep/TazUO`

### Workflow 1: Stable Release (`.github/workflows/release.yml`)

**Trigger**: Push a tag matching `v*` (e.g., `v1.0.0`)

```yaml
name: Stable Release

on:
  push:
    tags: ['v*']

jobs:
  build:
    strategy:
      matrix:
        include:
          - os: windows-latest
            rid: win-x64
          - os: ubuntu-latest
            rid: linux-x64
          - os: macos-latest
            rid: osx-arm64
          - os: macos-15-intel
            rid: osx-x64
    runs-on: ${{ matrix.os }}
    steps:
      - uses: actions/checkout@v4
        with:
          submodules: recursive
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      - run: dotnet publish src/ClassicUO.Client/ClassicUO.Client.csproj -c Release -r ${{ matrix.rid }} --self-contained
      - run: echo "${{ github.ref_name }}" > bin/Release/net10.0/${{ matrix.rid }}/publish/v.txt
      - uses: actions/upload-artifact@v4
        with:
          name: TazUO-${{ matrix.rid }}
          path: bin/Release/net10.0/${{ matrix.rid }}/publish/

  release:
    needs: build
    runs-on: ubuntu-latest
    permissions:
      contents: write
    steps:
      - uses: actions/download-artifact@v4
      - name: Zip artifacts
        run: |
          for dir in TazUO-*/; do
            name="${dir%/}"
            (cd "$dir" && zip -r "../${name}.zip" .)
          done
      - uses: softprops/action-gh-release@v2
        with:
          files: TazUO-*.zip
          generate_release_notes: true
```

### Workflow 2: Bleeding Edge (`.github/workflows/bleeding-edge.yml`)

**Trigger**: Push to `main`

```yaml
name: Bleeding Edge

on:
  push:
    branches: [main]
    tags-ignore: ['**']

concurrency:
  group: bleeding-edge
  cancel-in-progress: true

jobs:
  build:
    strategy:
      matrix:
        include:
          - os: windows-latest
            rid: win-x64
          - os: ubuntu-latest
            rid: linux-x64
          - os: macos-latest
            rid: osx-arm64
          - os: macos-15-intel
            rid: osx-x64
    runs-on: ${{ matrix.os }}
    steps:
      - uses: actions/checkout@v4
        with:
          submodules: recursive
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      - run: dotnet publish src/ClassicUO.Client/ClassicUO.Client.csproj -c Release -r ${{ matrix.rid }} --self-contained
      - name: Write version
        shell: bash
        run: echo "0.0.0-dev.$(date +%Y%m%d).$(echo ${{ github.sha }} | cut -c1-7)" > bin/Release/net10.0/${{ matrix.rid }}/publish/v.txt
      - uses: actions/upload-artifact@v4
        with:
          name: TazUO-${{ matrix.rid }}
          path: bin/Release/net10.0/${{ matrix.rid }}/publish/

  release:
    needs: build
    runs-on: ubuntu-latest
    permissions:
      contents: write
    steps:
      - uses: actions/download-artifact@v4
      - name: Zip artifacts
        run: |
          for dir in TazUO-*/; do
            name="${dir%/}"
            (cd "$dir" && zip -r "../${name}.zip" .)
          done
      - uses: softprops/action-gh-release@v2
        with:
          tag_name: TazUO-BleedingEdge
          prerelease: true
          make_latest: false
          files: TazUO-*.zip
          body: |
            Automated bleeding-edge build from main branch.
            Commit: ${{ github.sha }}
```

### Workflow 3: Branch Build (`.github/workflows/branch-build.yml`)

**Trigger**: Push to any branch except `main`

```yaml
name: Branch Build

on:
  push:
    branches-ignore: [main]
    tags-ignore: ['**']
  delete:

concurrency:
  group: branch-build-${{ github.ref_name }}
  cancel-in-progress: true

jobs:
  # Clean up release when branch is deleted
  cleanup:
    if: github.event_name == 'delete' && github.event.ref_type == 'branch'
    runs-on: ubuntu-latest
    permissions:
      contents: write
    steps:
      - name: Delete branch release
        env:
          GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}
        run: |
          BRANCH_TAG="branch-${{ github.event.ref }}"
          RELEASE_ID=$(gh api repos/${{ github.repository }}/releases/tags/${BRANCH_TAG} --jq '.id' 2>/dev/null || echo "")
          if [ -n "$RELEASE_ID" ]; then
            gh api -X DELETE repos/${{ github.repository }}/releases/${RELEASE_ID}
            gh api -X DELETE repos/${{ github.repository }}/git/refs/tags/${BRANCH_TAG} 2>/dev/null || true
          fi

  build:
    if: github.event_name == 'push'
    strategy:
      matrix:
        include:
          - os: windows-latest
            rid: win-x64
          - os: ubuntu-latest
            rid: linux-x64
          - os: macos-latest
            rid: osx-arm64
          - os: macos-15-intel
            rid: osx-x64
    runs-on: ${{ matrix.os }}
    steps:
      - uses: actions/checkout@v4
        with:
          submodules: recursive
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      - run: dotnet publish src/ClassicUO.Client/ClassicUO.Client.csproj -c Release -r ${{ matrix.rid }} --self-contained
      - name: Write version
        shell: bash
        run: |
          BRANCH_NAME="${GITHUB_REF_NAME}"
          echo "branch-${BRANCH_NAME}.$(date +%Y%m%d).$(echo ${{ github.sha }} | cut -c1-7)" > bin/Release/net10.0/${{ matrix.rid }}/publish/v.txt
      - uses: actions/upload-artifact@v4
        with:
          name: TazUO-${{ matrix.rid }}
          path: bin/Release/net10.0/${{ matrix.rid }}/publish/

  release:
    needs: build
    if: github.event_name == 'push'
    runs-on: ubuntu-latest
    permissions:
      contents: write
    steps:
      - uses: actions/download-artifact@v4
      - name: Zip artifacts
        run: |
          for dir in TazUO-*/; do
            name="${dir%/}"
            (cd "$dir" && zip -r "../${name}.zip" .)
          done
      - name: Sanitize branch name
        id: sanitize
        run: echo "name=$(echo '${{ github.ref_name }}' | sed 's/[^a-zA-Z0-9._-]/-/g')" >> $GITHUB_OUTPUT
      - uses: softprops/action-gh-release@v2
        with:
          tag_name: branch-${{ steps.sanitize.outputs.name }}
          prerelease: true
          make_latest: false
          files: TazUO-*.zip
          name: "Branch: ${{ github.ref_name }}"
          body: |
            Feature branch build: ${{ github.ref_name }}
            Commit: ${{ github.sha }}
```

---

## Part 5: Version Management

- **Stable**: `v.txt` = git tag (e.g., `v1.0.0`)
- **Bleeding edge**: `v.txt` = `0.0.0-dev.YYYYMMDD.SHA` (e.g., `0.0.0-dev.20260211.abc1234`)
- **Branch builds**: `v.txt` = `branch-<name>.YYYYMMDD.SHA` (e.g., `branch-autoloot-loot-profiles.20260211.abc1234`)
- Launcher compares local `v.txt` against remote release to detect updates

---

## Part 6: Launcher CI (`crameep/TUO-Launcher`)

Add a workflow that builds the launcher itself on tag push, so it can self-update:

- Build for Windows, macOS, Linux
- Create release with launcher binaries
- Launcher's `LAUNCHER_RELEASE_URL` points here for self-update checks

---

## Implementation Order

### Phase 1: CI/CD Pipeline (on `crameep/TazUO`)
1. Add `.github/workflows/release.yml` (stable releases)
2. Add `.github/workflows/bleeding-edge.yml` (main branch builds)
3. Add `.github/workflows/branch-build.yml` (feature branch builds)
4. Test: Push to a feature branch, verify release appears with platform zips
5. Test: Push to main, verify bleeding-edge release updates

### Phase 2: Launcher Fork (on `crameep/TUO-Launcher`)
6. Update `Constants.cs` - URL changes, remove NET472
7. Update `MainWindow.axaml` - branding (title + subtitle)
8. Remove NET472 from `ReleaseChannel` enum and UI
9. Test: Build launcher, verify it pulls from crameep/TazUO releases

### Phase 3: Branch Selector Feature (on `crameep/TUO-Launcher`)
10. Add `BRANCH_BUILDS_API_URL` constant
11. Add branch fetching logic to `UpdateHelper.cs`
12. Replace NET472 channel with "Feature Branch" in enum
13. Add branch dropdown UI to `MainWindow.axaml`
14. Add `SelectedBranch` to `LauncherSettings.cs` / `launcherdata.json`
15. Wire up download logic for branch releases
16. Test: Select feature branch in launcher, verify correct build downloads

### Phase 4: Launcher Self-Update
17. Add CI workflow to `crameep/TUO-Launcher` for launcher releases
18. Test: Tag launcher release, verify self-update works
