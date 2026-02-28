---
title: "Custom TazUO Launcher & CI/CD"
created: 2026-02-11
poured:
  - TazUo-mol-1n5d  # ci-stable-release
  - TazUo-mol-ul8o  # ci-bleeding-edge
  - TazUo-mol-582w  # ci-branch-build
  - TazUo-mol-oanw  # launcher-ci
  - TazUo-mol-8pf4  # launcher-urls
  - TazUo-mol-z3d4  # launcher-remove-net472
  - TazUo-mol-o5h8  # launcher-branding
  - TazUo-mol-ozop  # launcher-version-comparison
  - TazUo-mol-gtx9  # launcher-branch-enum
  - TazUo-mol-aadw  # launcher-branch-fetch
  - TazUo-mol-ybej  # launcher-branch-ui
iteration: 2
auto_discovery: false
auto_learnings: false
---
<project_specification>
<project_name>Custom TazUO Launcher & CI/CD</project_name>

  <overview>
    Fork the TazUO Launcher and set up CI/CD to distribute custom TazUO builds.
    Three main pieces: (1) GitHub Actions workflows on crameep/TazUO to build platform
    zips on push, (2) launcher fork changes to point at crameep repos with branding,
    (3) branch selector feature so any feature branch can be installed from the launcher.

    Two repos involved:
    - crameep/TazUO (current repo) — CI workflows only
    - crameep/TUO-Launcher (forked launcher) — all launcher code changes
  </overview>

  <context>
    <existing_patterns>
      - Launcher is C# / .NET 9.0 / Avalonia 11.2.1 (cross-platform desktop UI)
      - All GitHub API URLs are hardcoded in Constants.cs (internal static class CONSTANTS)
      - Release data fetched via UpdateHelper.cs using HttpClient with GitHub API v2022-11-28
      - ReleaseChannel enum: INVALID, MAIN, DEV, LAUNCHER, NET472 (in Enums.cs)
      - LauncherSaveFile stores DownloadChannel, LastSelectedProfileName, AutoDownloadUpdates
      - LauncherSaveFile uses atomic write (temp file + move) to launcherdata.json
      - GitHubReleaseData model maps GitHub release JSON (tag_name, assets[], prerelease, etc.)
      - PlatformHelper.GetPlatformZipName() returns "win-x64.zip", "linux-x64.zip", "osx-arm64.zip", or "osx-x64.zip"
      - Asset selection: first tries exact platform match, falls back to any zip starting with "TazUO"
      - Version comparison: reads v.txt from client directory, parses as Version (major.minor.build.patch)
      - Channel selection in UI is via radio menu items under "Update channel" in Tools menu
      - MainWindow uses Canvas layout with absolute positioning (800x450)
      - Logo at Canvas.Left=300, Canvas.Top=40, 200x200 rounded rectangle
      - ProfileSelector ComboBox at Canvas.Top=255
      - MVVM pattern with properties like MainChannelSelected, DevChannelSelected, LegacyChannelSelected
    </existing_patterns>
    <integration_points>
      - Constants.cs — all URL constants to change
      - Enums.cs — ReleaseChannel enum (remove NET472, add BRANCH)
      - UpdateHelper.cs — add branch release fetching, modify GetAllReleaseData()
      - LauncherSettings.cs / LauncherSaveFile — add SelectedBranch property
      - MainWindow.axaml — branding, branch dropdown UI
      - MainWindow.axaml.cs — channel selection logic, branch dropdown population
      - GitHubReleaseData.cs — no changes needed (same GitHub release format)
      - PlatformHelper.cs — no changes needed
    </integration_points>
    <new_technologies>
      - GitHub Actions: dotnet publish with RID-specific builds, matrix strategy
      - softprops/action-gh-release@v2 for creating/updating GitHub releases (use make_latest: false for pre-releases)
      - GitHub Actions delete event for branch cleanup
      - GitHub Actions concurrency groups to prevent race conditions on same-branch pushes
      - macos-13 runner is RETIRED — use macos-15-intel for Intel macOS builds
    </new_technologies>
    <conventions>
      - Launcher uses System.Text.Json for JSON serialization
      - Launcher namespace: TazUOLauncher
      - AXAML uses Avalonia data binding with x:Name references
      - All async operations use Task-based async/await
      - HTTP requests set User-Agent: "Public" and X-GitHub-Api-Version: "2022-11-28"
      - TazUO client project: dotnet publish -c Release -r RID --self-contained
    </conventions>
  </context>

  <tasks>

    <!-- ==================== PHASE 1: CI/CD on crameep/TazUO ==================== -->

    <task id="ci-stable-release" priority="1" category="infrastructure">
      <title>Add stable release GitHub Actions workflow</title>
      <description>
        Create .github/workflows/release.yml on crameep/TazUO that triggers on v* tag push.
        Builds 4 platform zips (win-x64, linux-x64, osx-arm64, osx-x64) using matrix strategy,
        writes version to v.txt, creates GitHub Release with all zips attached.

        Repo: crameep/TazUO
      </description>
      <steps>
        - Create .github/workflows/ directory
        - Create release.yml with tag trigger: on push tags ['v*']
        - Matrix strategy: windows-latest/win-x64, ubuntu-latest/linux-x64, macos-latest/osx-arm64, macos-15-intel/osx-x64
        - Each job: checkout with submodules recursive, setup-dotnet 10.0.x, dotnet publish -c Release -r RID --self-contained
        - Write git tag to v.txt in publish output (path: bin/Release/net10.0/RID/publish/v.txt)
        - Upload artifacts per platform
        - Release job: download all artifacts, zip each, attach to GitHub Release via softprops/action-gh-release@v2
      </steps>
      <test_steps>
        1. Push a v0.1.0 tag to crameep/TazUO
        2. Verify GitHub Actions workflow runs successfully
        3. Verify GitHub Release appears with 4 platform zips
        4. Download one zip, verify v.txt contains "v0.1.0"
        5. Verify the extracted build runs
      </test_steps>
      <review></review>
    </task>

    <task id="ci-bleeding-edge" priority="1" category="infrastructure">
      <title>Add bleeding-edge GitHub Actions workflow</title>
      <description>
        Create .github/workflows/bleeding-edge.yml on crameep/TazUO that triggers on push to main.
        Same build matrix as stable, but creates/updates the TazUO-BleedingEdge tagged pre-release.
        Version string uses format: 0.0.0-dev.YYYYMMDD.SHA7

        Repo: crameep/TazUO
      </description>
      <steps>
        - Create bleeding-edge.yml with trigger: on push branches [main], tags-ignore ['**']
        - Add concurrency group: bleeding-edge (cancel-in-progress: true)
        - Matrix strategy: windows-latest/win-x64, ubuntu-latest/linux-x64, macos-latest/osx-arm64, macos-15-intel/osx-x64
        - Version string: "0.0.0-dev.$(date +%Y%m%d).$(echo SHA | cut -c1-7)"
        - Release job uses tag_name: TazUO-BleedingEdge, prerelease: true, make_latest: false
        - Body includes commit SHA for traceability
      </steps>
      <test_steps>
        1. Push a commit to main on crameep/TazUO
        2. Verify workflow runs and creates/updates TazUO-BleedingEdge release
        3. Verify release is marked as pre-release
        4. Download a zip, verify v.txt has dev version format
        5. Push another commit, verify release updates (not duplicated)
      </test_steps>
      <review></review>
    </task>

    <task id="ci-branch-build" priority="2" category="infrastructure">
      <title>Add feature branch build GitHub Actions workflow</title>
      <description>
        Create .github/workflows/branch-build.yml on crameep/TazUO that triggers on push to
        any branch except main. Creates/updates a pre-release tagged "branch-BRANCHNAME".
        Also handles branch deletion by cleaning up the associated release.
        Version string: branch-NAME.YYYYMMDD.SHA7

        Repo: crameep/TazUO
      </description>
      <steps>
        - Create branch-build.yml with triggers: push (branches-ignore: [main], tags-ignore: ['**']) and delete
        - Add concurrency group per branch: branch-build-${{ github.ref_name }} (cancel-in-progress: true)
        - Sanitize branch name for use in tag: replace / with - (e.g., feature/foo → feature-foo)
        - Cleanup job: runs on delete event with ref_type==branch, deletes release and tag via gh API
        - Build job: matrix same as other workflows (macos-15-intel for osx-x64), runs on push event only
        - Version: "branch-${SANITIZED_NAME}.$(date +%Y%m%d).SHA7"
        - Release job: tag_name "branch-$SANITIZED_NAME", prerelease true, make_latest: false, name "Branch: ${{ github.ref_name }}"
      </steps>
      <test_steps>
        1. Push to a feature branch on crameep/TazUO
        2. Verify workflow runs and creates a "branch-BRANCHNAME" release
        3. Push again to same branch, verify release updates
        4. Delete the branch, verify release and tag are cleaned up
      </test_steps>
      <review></review>
    </task>

    <!-- ==================== PHASE 2: Launcher Fork - Basic Changes ==================== -->

    <task id="launcher-urls" priority="1" category="functional">
      <title>Update launcher Constants.cs URLs to crameep repos</title>
      <description>
        Change all GitHub API URLs in Constants.cs to point at crameep/TazUO and
        crameep/TUO-Launcher. Update web link URLs (GitHub, wiki).

        Repo: crameep/TUO-Launcher
        File: TazUOLauncher/Constants.cs
      </description>
      <steps>
        - Change MAIN_CHANNEL_RELEASE_URL to https://api.github.com/repos/crameep/TazUO/releases/latest
        - Change DEV_CHANNEL_RELEASE_URL to https://api.github.com/repos/crameep/TazUO/releases/tags/TazUO-BleedingEdge
        - Change LAUNCHER_RELEASE_URL to https://api.github.com/repos/crameep/TUO-Launcher/releases/latest
        - Change LAUNCHER_LATEST_URL to https://github.com/crameep/TUO-Launcher/releases/latest
        - Change GITHUB_URL to https://github.com/crameep/TazUO
        - Update or remove WIKI_URL
        - Remove NET472_CHANNEL_RELEASE_URL constant
      </steps>
      <test_steps>
        1. Build the launcher successfully
        2. Run launcher, verify it attempts to fetch from crameep/TazUO (check network or logs)
        3. Verify no references to PlayTazUO remain in Constants.cs
      </test_steps>
      <review></review>
    </task>

    <task id="launcher-remove-net472" priority="1" category="functional">
      <title>Remove NET472 legacy channel from launcher</title>
      <description>
        Remove the NET472 channel entirely from the launcher. This includes the enum value,
        UI menu items, and the data fetching call.

        Repo: crameep/TUO-Launcher
        Files: Enums.cs, UpdateHelper.cs, MainWindow.axaml, MainWindow.axaml.cs
      </description>
      <steps>
        - Remove NET472 from ReleaseChannel enum in Enums.cs
        - Remove TryGetReleaseData(ReleaseChannel.NET472) call from GetAllReleaseData() in UpdateHelper.cs
        - Remove the NET472 URL-to-channel mapping in TryGetReleaseData(ReleaseChannel)
        - Remove "Legacy Channel" radio menu item from MainWindow.axaml (under Update channel menu)
        - Remove SetLegacyChannelClicked() handler in MainWindow.axaml.cs
        - Remove LegacyChannelSelected property binding
        - Remove "Install latest legacy build" menu item if present
      </steps>
      <test_steps>
        1. Build succeeds with no errors
        2. Run launcher, verify only Main and Dev channel options exist
        3. Verify no NET472 references remain in code (search for NET472 and Legacy)
      </test_steps>
      <review></review>
    </task>

    <task id="launcher-branding" priority="2" category="style">
      <title>Add Crameep's Build branding to launcher</title>
      <description>
        Update window title and add "Crameep's Build" subtitle below the logo.
        Minimal branding - keep stock look, just make it distinguishable.

        Repo: crameep/TUO-Launcher
        File: TazUOLauncher/Windows/MainWindow.axaml
      </description>
      <steps>
        - Change window Title from "TazUOLauncher" to "TazUO - Crameep's Build"
        - Add TextBlock after the logo Rectangle: Canvas.Left=300, Canvas.Top=245, Width=200, centered, white, FontSize=12, Opacity=0.75, text "Crameep's Build"
        - Nudge ProfileSelector ComboBox down by ~15px if subtitle overlaps (Canvas.Top 255 -> 270)
        - Adjust any elements positioned relative to ProfileSelector bounds if needed
      </steps>
      <test_steps>
        1. Build and run launcher
        2. Verify window title bar shows "TazUO - Crameep's Build"
        3. Verify "Crameep's Build" text appears centered below the logo
        4. Verify text doesn't overlap with profile selector dropdown
        5. Verify overall layout still looks clean
      </test_steps>
      <review></review>
    </task>

    <task id="launcher-version-comparison" priority="1" category="functional">
      <title>Rewrite version comparison to handle new version formats</title>
      <description>
        The launcher currently uses System.Version.Parse() to compare local v.txt against remote
        release versions. Our new version formats (0.0.0-dev.YYYYMMDD.SHA, branch-NAME.YYYYMMDD.SHA)
        are NOT valid System.Version strings and will throw FormatException.

        Need to replace System.Version-based comparison with string-based comparison that handles
        all three version formats: stable (v1.0.0), bleeding-edge (0.0.0-dev.YYYYMMDD.SHA),
        and branch (branch-NAME.YYYYMMDD.SHA).

        Repo: crameep/TUO-Launcher
        File: TazUOLauncher/Utility/UpdateHelper.cs (and any version comparison call sites)
      </description>
      <steps>
        - Find all call sites that use System.Version.Parse() on v.txt content
        - For stable channel: extract version tag from release, compare as SemVer (strip 'v' prefix, compare major.minor.patch)
        - For dev channel: compare date+SHA suffix — if local v.txt matches remote tag_name's date+SHA, no update needed
        - For branch channel: compare full version string — if local matches remote, no update needed
        - Simplest approach: for dev/branch, just compare full v.txt string against expected version string — any mismatch means update available
        - Handle edge case: switching channels always triggers update (dev version != stable version)
        - Add helper method like IsUpdateAvailable(string localVersion, string remoteVersion, ReleaseChannel channel)
      </steps>
      <test_steps>
        1. Build succeeds
        2. Stable: local "v1.0.0" vs remote "v1.0.1" → update available
        3. Stable: local "v1.0.1" vs remote "v1.0.1" → no update
        4. Dev: local "0.0.0-dev.20260211.abc1234" vs different SHA → update available
        5. Dev: same version string → no update
        6. Branch: same version string → no update
        7. Cross-channel: switching from dev to stable → update available
      </test_steps>
      <review></review>
    </task>

    <!-- ==================== PHASE 3: Branch Selector Feature ==================== -->

    <task id="launcher-branch-enum" priority="2" category="functional">
      <title>Add BRANCH release channel to launcher enum and settings</title>
      <description>
        Add a BRANCH value to the ReleaseChannel enum and a SelectedBranch property
        to LauncherSaveFile for persisting the user's branch selection.

        Repo: crameep/TUO-Launcher
        Files: Enums.cs, LauncherSettings.cs, Constants.cs
      </description>
      <steps>
        - Add BRANCH to ReleaseChannel enum in Enums.cs (replacing NET472's slot or as new value)
        - Add SelectedBranch string property to LauncherSaveFile (default empty)
        - Add BRANCH_BUILDS_API_URL constant to Constants.cs: "https://api.github.com/repos/crameep/TazUO/releases"
        - Ensure LauncherSaveFile serialization handles new property (System.Text.Json should auto-handle)
      </steps>
      <test_steps>
        1. Build succeeds
        2. Verify launcherdata.json can round-trip with SelectedBranch field
        3. Verify old launcherdata.json without SelectedBranch loads without error (defaults to empty)
      </test_steps>
      <review></review>
    </task>

    <task id="launcher-branch-fetch" priority="2" category="functional">
      <title>Add branch release fetching logic to UpdateHelper</title>
      <description>
        Add methods to UpdateHelper.cs that fetch all releases from the GitHub API,
        filter to those with "branch-" tag prefix, and return a list of available
        branch builds. Integrate with the existing GetAllReleaseData flow.

        Repo: crameep/TUO-Launcher
        File: TazUOLauncher/Utility/UpdateHelper.cs
      </description>
      <steps>
        - Add static field: List of GitHubReleaseData for branch releases
        - Add method: GetBranchReleases() — fetches BRANCH_BUILDS_API_URL (paginated, up to 100 releases), deserializes as GitHubReleaseData[], filters to tag_name starting with "branch-"
        - Cache branch list in memory with 5-minute TTL to avoid hitting GitHub API rate limit (60 req/hr unauthenticated)
        - Add method: GetBranchNames() — returns list of branch names (strip "branch-" prefix from tag_name)
        - Add method: GetBranchReleaseData(string branchName) — returns GitHubReleaseData for specific branch tag
        - Call GetBranchReleases() in GetAllReleaseData() alongside other channel fetches
        - Handle BRANCH channel in TryGetReleaseData() — look up from cached branch releases using SelectedBranch
        - Handle BRANCH channel in DownloadAndInstallZip() — same download logic, just different release source
        - Handle error case: selected branch no longer has a build (branch was deleted) — show message, fall back to Main
      </steps>
      <test_steps>
        1. Build succeeds
        2. With branch releases on crameep/TazUO, verify GetBranchNames() returns correct list
        3. Verify branch release data includes platform assets
        4. Verify DownloadAndInstallZip works with BRANCH channel
      </test_steps>
      <review></review>
    </task>

    <task id="launcher-branch-ui" priority="2" category="functional">
      <title>Add branch selector UI to launcher</title>
      <description>
        Add "Feature Branch" as a channel option in the Tools menu. When selected,
        show a dropdown/submenu of available branches. Wire up selection to download
        the correct branch build.

        Repo: crameep/TUO-Launcher
        Files: MainWindow.axaml, MainWindow.axaml.cs
      </description>
      <steps>
        - Add "Feature Branch" radio menu item under "Update channel" in Tools menu (where Legacy was)
        - Add BranchChannelSelected bool property for MVVM binding
        - Add SetBranchChannelClicked() handler — sets DownloadChannel to BRANCH
        - When BRANCH channel is active, populate a submenu or ComboBox with available branch names from UpdateHelper.GetBranchNames()
        - On branch selection: save to LauncherSaveFile.SelectedBranch, call RecheckAfterChannelUpdated()
        - Update version display to show branch name when on BRANCH channel
        - Update download button logic to work with BRANCH channel
        - Handle case where no branch builds exist (show message)
      </steps>
      <test_steps>
        1. Build and run launcher
        2. Open Tools menu, verify "Feature Branch" option appears under Update channel
        3. Select Feature Branch, verify branch list populates
        4. Select a specific branch, verify version info updates
        5. Click download, verify correct branch build downloads and installs
        6. Restart launcher, verify branch selection persisted
        7. Switch back to Main channel, verify normal behavior resumes
      </test_steps>
      <review></review>
    </task>

    <!-- ==================== PHASE 4: Launcher Self-Update CI ==================== -->

    <task id="launcher-ci" priority="3" category="infrastructure">
      <title>Add CI workflow to build and release the launcher itself</title>
      <description>
        Add a GitHub Actions workflow to crameep/TUO-Launcher that builds the launcher
        for all platforms on tag push. Creates a release so the launcher can self-update.

        Repo: crameep/TUO-Launcher
      </description>
      <steps>
        - Create .github/workflows/release.yml in crameep/TUO-Launcher
        - Trigger on v* tag push
        - Matrix: windows-latest/win-x64, ubuntu-latest/linux-x64, macos-latest/osx-arm64, macos-15-intel/osx-x64
        - dotnet publish TazUOLauncher/TazUOLauncher.csproj -c Release -r RID --self-contained
        - Zip each platform output
        - Create GitHub Release with all zips
        - Launcher naming: TUO-Launcher-win-x64.zip etc.
      </steps>
      <test_steps>
        1. Push a v0.1.0 tag to crameep/TUO-Launcher
        2. Verify workflow builds successfully
        3. Verify release appears with platform zips
        4. Download and run launcher from the release
        5. Verify launcher detects itself as up-to-date
      </test_steps>
      <review></review>
    </task>

  </tasks>

  <success_criteria>
    - Pushing to main on crameep/TazUO auto-publishes bleeding-edge builds for all platforms
    - Tagging a version creates a stable release with all platform zips
    - Feature branch pushes create downloadable branch builds
    - Launcher downloads and installs correct platform build from crameep/TazUO
    - Branch selector lets users install any active feature branch build
    - Launcher self-updates from crameep/TUO-Launcher releases
    - "Crameep's Build" branding visible in launcher
  </success_criteria>

</project_specification>
