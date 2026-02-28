# Repository Guidelines

## Project Structure & Module Organization
Core code lives in `src/` and is split into focused projects: `ClassicUO.Client` (game/client logic), `ClassicUO.Renderer` (rendering), `ClassicUO.IO` (file/audio IO), `ClassicUO.Utility` (shared helpers), `ClassicUO.Assets` (embedded assets), and `ClassicUO.CUOAPI` (API surface). Unit tests are in `tests/ClassicUO.UnitTests/`. Third-party dependencies are in `external/` (git submodules). Build outputs go to `bin/` and project-local `obj/` folders.

## Build, Test, and Development Commands
- `./build.sh` builds `ClassicUO.sln` in Debug mode and initializes submodules if needed.
- `./build.sh release` builds Release artifacts.
- `dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj -c Release` builds only the client project.
- `dotnet test tests/ClassicUO.UnitTests/` runs xUnit tests.
- `dotnet test tests/ClassicUO.UnitTests/ --collect:"XPlat Code Coverage"` generates coverage output via Coverlet.
- `./format.sh` runs `dotnet format style` across all main source projects.

## Coding Style & Naming Conventions
Formatting is governed by `.editorconfig`.
- Use spaces, not tabs; C# uses 4-space indentation.
- Prefer LF line endings for source files.
- Follow C# naming conventions: `PascalCase` for types/methods/properties, interfaces prefixed with `I`.
- Keep file names aligned with primary type names when practical.

Run formatting before opening a PR, especially after broad refactors.

## Testing Guidelines
Tests use xUnit + FluentAssertions. Place new tests under `tests/ClassicUO.UnitTests/` in folders that mirror the source area (for example, `IO/` tests for `ClassicUO.IO`). Use descriptive test file names ending in `Test.cs` (e.g., `StackDataReaderTest.cs`). Add regression tests for bug fixes and behavior changes.

## Commit & Pull Request Guidelines
This repo follows a Conventional Commit style in practice: `feat: ...`, `fix: ...`, `docs: ...` (lowercase type, concise summary). Keep commits focused and buildable.

For PRs, include:
- Clear problem/solution summary and risk notes.
- Linked tracking item (`bd` issue ID when applicable).
- Test evidence (`dotnet test` results, and screenshots for UI changes).

## Issue Tracking Workflow
Use beads (`bd`) for task state:
`bd ready`, `bd show <id>`, `bd update <id> --status in_progress`, `bd close <id>`, `bd sync`.
