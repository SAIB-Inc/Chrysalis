# Contributing to Chrysalis

## Branch Workflow

1. **Always work in a PR branch** — never commit directly to `main`.
2. Create a feature branch from `main`:
   ```bash
   git checkout main
   git pull origin main
   git checkout -b <type>/<short-description>
   ```
3. Push your branch and open a Pull Request.
4. All changes must be reviewed and merged via PR.

## Commit Convention

We use [Conventional Commits](https://www.conventionalcommits.org/).

Format:
```
<type>(<scope>): <description>

[optional body]

[optional footer(s)]
```

### Types

| Type | Description |
|------|-------------|
| `feat` | A new feature |
| `fix` | A bug fix |
| `docs` | Documentation only changes |
| `style` | Code style changes (formatting, no logic change) |
| `refactor` | Code change that neither fixes a bug nor adds a feature |
| `perf` | Performance improvement |
| `test` | Adding or updating tests |
| `build` | Changes to build system or dependencies |
| `ci` | CI/CD configuration changes |
| `chore` | Other changes that don't modify src or test files |

### Scopes

| Scope | Description |
|-------|-------------|
| `cbor` | CBOR serialization library |
| `codegen` | Source generator for CBOR types |
| `network` | Ouroboros mini-protocols and node communication |
| `wallet` | Address generation, keys, and credentials |
| `tx` | Transaction building and submission |
| `plutus` | Plutus script evaluation (Rust FFI) |

### Examples

```
feat(network): add N2N ChainSync keepalive
fix(wallet): correct address type nibble encoding
docs: update README with Byron era support
refactor(cbor): extract union deserialization into helper
test(cbor): add Byron block round-trip tests
build: upgrade to net10 and refresh deps
```

## Code Quality

All Roslyn IDE suggestions are enforced as build errors. The build will fail if your code has style violations.

### Before committing

Run `dotnet format` to auto-fix all style violations:

```bash
dotnet format Chrysalis.slnx
```

### What gets enforced

The `.editorconfig` at the repo root promotes **all** Roslyn style rules to warnings, and `Directory.Build.props` sets `TreatWarningsAsErrors=true`. This means:

- **File-scoped namespaces** — `namespace Foo;` not `namespace Foo { }`
- **Unused code** — no unused usings, parameters, variables, or private members
- **XML documentation** — all public types and members must have `/// <summary>` docs (test projects are exempt)

### How it works

```
.editorconfig                → sets ALL IDE rule severities to warning
Directory.Build.props        → EnforceCodeStyleInBuild=true (runs at build time)
                             → TreatWarningsAsErrors=true (warnings become errors)
                             → GenerateDocumentationFile=true (enables CS1591)
```

### Tests

- Test classes must be `public` (xUnit requirement)
- XML doc comments are not required in test projects

## Merging

- **Always squash merge** PRs into `main`.
- The squash commit message must follow conventional commit format.
- **Always delete the branch** after merging.

## Summary

```
main (protected)
  └── feat/my-feature (PR branch)
        ├── feat(scope): commit 1
        ├── fix(scope): commit 2
        └── squash merge → main → delete branch
```
