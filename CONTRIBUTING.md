# Contributing to Chrysalis

Thank you for your interest in contributing to Chrysalis! This document provides guidelines and processes for contributing to the project.

## Table of Contents

- [Code of Conduct](#code-of-conduct)
- [Getting Started](#getting-started)
- [Development Workflow](#development-workflow)
- [Commit Conventions](#commit-conventions)
- [Pull Request Process](#pull-request-process)
- [Release Process](#release-process)

## Code of Conduct

Please be respectful and constructive in all interactions. We're building software for the Cardano community and value collaboration.

## Getting Started

### Prerequisites

- .NET 10.0 SDK
- Rust toolchain (for Plutus module)
- Git

### Local dependency staging

Before restoring a fresh clone, obtain the approved local builds of
`SAIB.Cbor.3.1.0.nupkg` and `SAIB.Blake2Fast.4.0.0.nupkg` and place them in
`../local-packages/`, a sibling of the repository directory. These versions are
not yet published. `NuGet.Config` deliberately prevents falling back to nuget.org
for them; a missing local package makes restore fail. The release MR removes
this staging mapping after publication. Do not repack a version with different content.

### Setup

1. Fork the repository
2. Clone your fork:
   ```bash
   git clone https://github.com/YOUR_USERNAME/Chrysalis.git
   cd Chrysalis
   ```
3. Add upstream remote:
   ```bash
   git remote add upstream https://github.com/SAIB-Inc/Chrysalis.git
   ```
4. Restore and build:
   ```bash
   dotnet restore
   dotnet build
   ```

### Building the Plutus Native Libraries

```bash
cd src/Chrysalis.Plutus
chmod +x build-rs.sh
./build-rs.sh
```

## Development Workflow

1. Create a feature branch from `main`:
   ```bash
   git checkout main
   git pull upstream main
   git checkout -b feature/your-feature-name
   ```

2. Make your changes and [run the offline tests](#tests).

3. Commit your changes following the [commit conventions](#commit-conventions)

4. Push to your fork and create a Pull Request

### Tests

During the staged xUnit v3 migration, both test generations use VSTest. Keep
`Category!=Integration` to exclude opt-in integration tests; do not enable the
native Microsoft.Testing.Platform runner until all projects have converted.

```bash
dotnet build -c Release -warnaserror -m:8
mkdir -p out
results=$(mktemp -d out/test-results.XXXXXX)
dotnet test -c Release --no-build --no-restore --filter "Category!=Integration" --logger trx --results-directory "$results" -m:8
python3 scripts/test-manifest.py "$results" > "$results/manifest.txt"
```

Compare manifests from the same operating system with `diff -u before.txt after.txt`.
The manifest keeps duplicate cases and records a JSON `[name, outcome]` pair per line,
so newly skipped or failed cases change the comparison.
For a project referencing `coverlet.collector`, append `--collect:"XPlat Code Coverage"`
to collect coverage.

## Commit Conventions

We use [Conventional Commits](https://www.conventionalcommits.org/) for clear and consistent commit history.

### Format

```
<type>(<scope>): <description>

[optional body]

[optional footer]
```

### Types

| Type | Description |
|------|-------------|
| `feat` | A new feature |
| `fix` | A bug fix |
| `docs` | Documentation changes |
| `style` | Code style changes (formatting, semicolons, etc.) |
| `refactor` | Code refactoring without feature changes |
| `perf` | Performance improvements |
| `test` | Adding or updating tests |
| `chore` | Maintenance tasks (dependencies, builds, etc.) |

### Scopes

Common scopes for this project:

- `cbor` - Chrysalis.Cbor module
- `codegen` - Chrysalis.Cbor.CodeGen module
- `network` - Chrysalis.Network module
- `tx` - Chrysalis.Tx module
- `plutus` - Chrysalis.Plutus module
- `wallet` - Chrysalis.Wallet module
- `release` - Release-related changes

### Examples

```
feat(tx): add methods to retrieve UTXOs by payment key
fix(cbor): handle empty asset name in deserialization
chore(release): bump version to v1.0.4-alpha
docs: update README with new examples
```

## Pull Request Process

1. **Create a descriptive PR title** following commit conventions
2. **Fill out the PR template** with:
   - Description of changes
   - Related issues
   - Testing performed
3. **Ensure CI passes** - all tests must pass
4. **Request review** from maintainers
5. **Address feedback** promptly
6. **Squash and merge** when approved

### PR Requirements

- All tests must pass
- Code must build without errors
- Follow existing code style and patterns
- Update documentation if needed
- Add tests for new functionality

## Release Process

> **IMPORTANT**: Code reaches `main` only through Pull Requests. A release is then cut by **pushing a version tag to `main`** — the `release.yml` workflow builds, tests, packs, and publishes to NuGet. There is **no version-bump commit and no separate release PR**.

### Version Scheme

We use one shared semantic version in the **`v2.x` series**:

- **Alpha**: Start at `v2.0.0-alpha`. Each later alpha takes its own rising `v2.Y.Z-alpha` number: the **next minor** for a feature release (patch reset to zero), the **next patch** for a fix. **`v2.0.0-alpha.N` is not used.**
- **Beta**: `v2.Y.Z-beta` - Feature complete, bug fixes only
- **Stable**: Take the **next minor after the last alpha**, without a suffix; for example, last alpha `v2.3.x-alpha` → stable `v2.4.0`.

### Step-by-Step Release Process

> **No version file to bump.** `Directory.Build.props` stays at the `0.0.0-dev` placeholder; the published version is taken from the tag (the release workflow packs with `-p:Version=<tag>`).

#### 1. Merge the change(s)

Get the feature/fix PR(s) reviewed, green on CI, and squash-merged into `main` (see the Pull Request Process above). There is no separate release branch or version-bump commit.

#### 2. Create the Git Tag / GitHub Release

From an up-to-date `main` (`git checkout main && git pull origin main`):

```bash
gh release create vX.Y.Z-alpha \
  --title "vX.Y.Z-alpha" \
  --prerelease \
  --notes ""
```

Use `v2.0.0-alpha` first. For each later alpha, take the **next minor for a feature release** or the **next patch for a fix**, not `v2.0.0-alpha.N`. The stable takes the **next minor after the last alpha**, without a suffix (for example, after `v2.3.x-alpha`, use `v2.4.0`). Omit `--prerelease` only for that stable tag; the workflow auto-marks any tag containing `-` as a pre-release.

Leave the body empty — the release workflow's `softprops/action-gh-release` step runs on the tag push and populates the release notes via GitHub's auto-changelog. Passing `--generate-notes` here would double the body because the workflow would then append a second copy.

The tag also triggers build, test, pack, and publish of all packages.

#### 6. Verify CI/CD

The release will trigger the CI/CD pipeline which:
1. Builds all projects
2. Runs tests
3. Packs the published NuGet packages:
   - `Chrysalis`
   - `Chrysalis.Cbor`
   - `Chrysalis.Cbor.CodeGen`
   - `Chrysalis.Crypto`
   - `Chrysalis.Network`
   - `Chrysalis.Plutus`
   - `Chrysalis.Tx`
   - `Chrysalis.Tx.Providers.Blockfrost`
   - `Chrysalis.Tx.Providers.Kupmios`
   - `Chrysalis.Tx.Providers.Ouroboros`
   - `Chrysalis.Wallet`
4. Pushes to NuGet.org (on release publish)

### Release Checklist

- [ ] Update [`docs/MANIFEST.md`](docs/MANIFEST.md) for this release and complete its pre-release checklist (matrix, node-pinned dependencies, local-package integration, performance/results evidence and known limitations); obtain the owner's word before any push or publication.
- [ ] Feature/fix PR(s) merged to `main`, CI green
- [ ] `main` pulled locally and up to date
- [ ] `gh release create vX.Y.Z[-alpha] --prerelease --notes ""` (empty notes — the workflow generates them; passing `--generate-notes` here would duplicate the body)
- [ ] Release marked as pre-release (auto for any tag containing `-`)
- [ ] Release workflow succeeded: build, test, pack, NuGet push
- [ ] NuGet packages visible on NuGet.org
- [ ] Release notes reviewed for accuracy

### Hotfix Releases

For urgent fixes:

1. Create a hotfix branch from `main`
2. Apply the fix
3. Create the next hotfix tag (increment patch version)
4. Follow the standard PR and release process

### What NOT to Do

- **Never push code directly to `main`** - Always use Pull Requests
- **Never force push to `main`** - This destroys history
- **Never hand-edit the published version** - it comes from the tag, not a file
- **Never tag `main` before the change PR(s) are merged and CI is green**

## Questions?

If you have questions about contributing, please open an issue or reach out to the maintainers.

---

Thank you for contributing to Chrysalis!
