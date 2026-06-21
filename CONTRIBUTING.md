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

2. Make your changes and ensure tests pass:
   ```bash
   dotnet test
   ```

3. Commit your changes following the [commit conventions](#commit-conventions)

4. Push to your fork and create a Pull Request

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

We use semantic versioning with pre-release suffixes (current series: `v1.7.x`):

- **Alpha**: `vX.Y.Z-alpha` - Active development, may have breaking changes
- **Beta**: `vX.Y.Z-beta` - Feature complete, bug fixes only
- **Stable**: `vX.Y.Z` - Production ready

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

Pick the next version per the scheme above (increment the patch for a fix). Omit `--prerelease` only for a stable `vX.Y.Z` tag (no `-` suffix); the workflow auto-marks any tag containing `-` as a pre-release.

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
   - `Chrysalis.Wallet`
4. Pushes to NuGet.org (on release publish)

### Release Checklist

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
