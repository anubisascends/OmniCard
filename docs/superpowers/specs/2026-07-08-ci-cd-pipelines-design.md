# CI/CD Pipelines — Design Spec

## Context

OmniCard has no CI/CD. The goal is to add GitHub Actions workflows that automatically build and test on every PR, publish a versioned `.exe` on GitHub Releases, and report code coverage. The repo is public. eBay API keys are already secure (User Secrets only, empty in appsettings.json) and do not need to be in CI.

## Workflow 1: CI (Build + Test + Coverage)

**File:** `.github/workflows/ci.yml`

**Triggers:** Push to `master`, all pull requests.

**Runner:** `windows-latest` (required — WPF projects can't build on Linux).

**Steps:**
1. Checkout code
2. Setup .NET 10 SDK
3. `dotnet restore OmniCard.slnx`
4. `dotnet build OmniCard.slnx --no-restore -c Release`
5. `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --no-build -c Release --collect:"XPlat Code Coverage" --results-directory ./coverage`
6. Upload coverage report as workflow artifact
7. (Optional future enhancement: post coverage summary as PR comment)

**Coverage tooling:** The test project already has `coverlet.collector` installed. The `--collect:"XPlat Code Coverage"` flag produces a Cobertura XML report. Upload it as a build artifact so it can be downloaded and reviewed.

**Pre-existing test failure:** `SearchCollection_ScryfallSyntax_SetFilter` currently fails. This must be fixed or skipped before CI is enabled, otherwise CI will always be red.

## Workflow 2: Release (Publish Versioned .exe)

**File:** `.github/workflows/release.yml`

**Triggers:** When a GitHub Release is published (tag pattern `v*`).

**Runner:** `windows-latest`.

**Steps:**
1. Checkout code
2. Setup .NET 10 SDK
3. Extract version from tag: strip `v` prefix from tag name (e.g., `v1.2.3` → `1.2.3`)
4. `dotnet publish OmniCard/OmniCard.csproj -c Release -r win-x64 /p:Version={version}` — the csproj already has `PublishSingleFile`, `SelfContained`, `EnableCompressionInSingleFile` configured
5. Rename output to `OmniCard-v{version}-win-x64.exe`
6. Upload the `.exe` to the GitHub Release as a downloadable asset using `gh release upload` or the `softprops/action-gh-release` action

**Version injection:** The `/p:Version={version}` MSBuild property sets `AssemblyVersion`, `FileVersion`, and `InformationalVersion` from the git tag. No csproj changes needed — MSBuild accepts this as a command-line override.

## eBay API Keys — Security Model

**Current state (already secure):**
- `appsettings.json` ships with empty eBay settings — committed to git, no secrets
- Actual keys are in User Secrets (`%APPDATA%\Microsoft\UserSecrets\`) — never in source control
- `App.xaml.cs` calls `config.AddUserSecrets<App>()` which overrides empty appsettings values at runtime

**CI/CD impact:** None. Tests use fakes/mocks, not real eBay APIs. The published `.exe` ships with empty settings. Each user configures their own keys locally via User Secrets.

**Public repo protection:** On public repos, GitHub automatically blocks fork PRs from reading repository secrets. Even if secrets were added in the future (e.g., for integration tests), fork PRs cannot access them.

**If integration tests are ever needed:**
1. Add secrets in repo Settings > Secrets and Variables > Actions: `EBAY_APP_ID`, `EBAY_CERT_ID`, `EBAY_DEV_ID`, `EBAY_RUNAME`
2. Inject in workflow via `env:` block or write to a temp `secrets.json`
3. Fork PRs still cannot read these — GitHub enforces this at the platform level

## Files to Create/Modify

| File | Action |
|------|--------|
| `.github/workflows/ci.yml` | Create |
| `.github/workflows/release.yml` | Create |
| Pre-existing failing test | Fix or skip |

## Verification

1. Push to a branch, open a PR — CI workflow should trigger, build, test, produce coverage artifact
2. Create a GitHub Release with tag `v0.1.0` — Release workflow should trigger, produce `OmniCard-v0.1.0-win-x64.exe` attached to the release
3. Download the `.exe` from the release page, verify it launches
