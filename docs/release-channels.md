# Release channels

One concept, one name, everywhere. The launcher menu, the GitHub Actions workflow names, and
the release titles all use the same three words.

| Launcher menu | Built from | Workflow (Actions tab) | Release tag | Release title |
|---|---|---|---|---|
| **Stable** | `main` | Stable Release | `TazUO-AutoBuild` | `Stable v<version>` |
| **Bleeding Edge** | `dev` | Bleeding Edge Release | `TazUO-BleedingEdge` | `Bleeding Edge v<version>` |
| **Feature Branch** | any other branch | Feature Branch Build | `branch-<branch-name>` | `Feature Branch: <branch>` |

Pick a channel from the launcher's channel menu. "Stable" is the default.

## How a build reaches you

Every channel starts with the **CI Build and Test** workflow. The two deploy workflows trigger on
that workflow completing successfully, via `workflow_run`:

```
push to main  ->  CI Build and Test  ->  Stable Release         ->  TazUO-AutoBuild    ->  launcher "Stable"
push to dev   ->  CI Build and Test  ->  Bleeding Edge Release  ->  TazUO-BleedingEdge ->  launcher "Bleeding Edge"
push to any
other branch  ->  Feature Branch Build                          ->  branch-<name>      ->  launcher "Feature Branch"
```

Two consequences worth knowing:

- `CI Build and Test` must run on both `main` and `dev`. It is listed in the `push.branches` of
  `.github/workflows/build-test.yml`. Remove either branch and that channel silently stops
  publishing, because the deploy workflow never receives its `workflow_run` event.
- The deploy workflows reference the CI workflow by its **display name**, not its filename
  (`workflows: [CI Build and Test]`). Renaming the `name:` in `build-test.yml` requires updating
  every referencing workflow in the same commit.

Feature Branch builds skip CI and run directly on push, and `branch-build.yml` ignores `main` and
`dev` so it never competes with the two release channels.

## Values the launcher depends on

These are a contract with launchers already installed on people's machines. Renaming any of them
breaks updates for everyone who has not manually reinstalled, so they are deliberately *not* named
after the channel words above:

| Value | Where it lives | Used for |
|---|---|---|
| `TazUO-AutoBuild` | `tuo-deploy.yml` | Must remain the newest non-prerelease, because Stable reads GitHub's `releases/latest` |
| `TazUO-BleedingEdge` | `tuo-dev-deploy.yml` | Fetched by exact tag |
| `branch-` prefix | `branch-build.yml` | How the launcher discovers Feature Branch builds |
| `win-x64.zip`, `linux-x64.zip`, `osx-arm64.zip` | asset names | Matched by `EndsWith` against the running platform |
| `ReleaseChannel` numbers | launcher `Enums.cs` | Persisted as integers in the launcher save file |

Stable is whatever GitHub reports as the latest non-prerelease release, so the Stable Release
workflow publishes with `makeLatest: true` and `prerelease: false`. Bleeding Edge and Feature
Branch builds are always prereleases and never claim `latest`.
