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

- .NET 9.0 SDK
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

> **IMPORTANT**: All releases MUST go through the Pull Request process. Never push version changes directly to `main`.

### Version Scheme

We use semantic versioning with pre-release suffixes:

- **Alpha**: `v1.0.x-alpha` - Active development, may have breaking changes
- **Beta**: `v1.0.x-beta` - Feature complete, bug fixes only
- **Stable**: `v1.0.x` - Production ready

### Step-by-Step Release Process

#### 1. Create a Release Branch

```bash
git checkout main
git pull origin main
git checkout -b release/v1.0.X-alpha
```

#### 2. Prepare the Release Changes

If the release requires packaging or workflow changes, make them on the release branch and commit them with a conventional commit message.

#### 3. Create a Pull Request

```bash
git push origin release/v1.0.X-alpha
```

Then create a PR on GitHub:
- **Title**: `chore(release): bump version to v1.0.X-alpha`
- **Description**: List the changes included in this release
- **Target branch**: `main`

#### 4. Merge the PR

After approval, merge the PR into `main`.

#### 5. Create the Git Tag / GitHub Release

```bash
gh release create v1.0.X-alpha \
  --title "v1.0.X-alpha" \
  --generate-notes \
  --prerelease
```

The tag triggers the release workflow, which builds, tests, packs, and publishes the packages. The `--generate-notes` flag automatically creates a changelog from merged PRs since the last release.

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

- [ ] Release changes committed with proper message format
- [ ] Pull Request created and approved
- [ ] PR merged to `main`
- [ ] GitHub release created with `--generate-notes`
- [ ] Release marked as pre-release (if alpha/beta)
- [ ] NuGet packages published for the umbrella package and supported leaf packages
- [ ] Release notes reviewed for accuracy

### Hotfix Releases

For urgent fixes:

1. Create a hotfix branch from `main`
2. Apply the fix
3. Create the next hotfix tag (increment patch version)
4. Follow the standard PR and release process

### What NOT to Do

- **Never push directly to `main`** - Always use Pull Requests
- **Never force push to `main`** - This destroys history
- **Never skip the PR for release changes** - The PR provides audit trail
- **Never create releases without the release PR merged first**

## Questions?

If you have questions about contributing, please open an issue or reach out to the maintainers.

---

Thank you for contributing to Chrysalis!
