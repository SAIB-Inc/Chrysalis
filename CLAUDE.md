# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Chrysalis is a comprehensive .NET ecosystem for Cardano blockchain development. It provides everything .NET developers need to interact with the Cardano blockchain, from low-level CBOR serialization to high-level transaction building and smart contract interaction.

The project started as a CBOR serialization library but has evolved into a complete toolkit that includes:

1. **Data Serialization** - Core CBOR serialization for Cardano data structures
2. **Network Communication** - Direct connection to Cardano nodes through Ouroboros mini-protocols
3. **Wallet Management** - Address generation, key derivation, and credential handling
4. **Transaction Building** - Tools for building and signing Cardano transactions
5. **Smart Contract Integration** - Evaluation and validation of Plutus scripts

Chrysalis aims to be for .NET what Pallas is for Rust - a complete set of native building blocks for Cardano development.

## Build Commands

```bash
# Restore dependencies
dotnet restore

# Build the entire solution
dotnet build

# Build specific project
dotnet build src/Chrysalis.Cbor/Chrysalis.Cbor.csproj

# Build in release mode
dotnet build -c Release
```

## Test Commands

```bash
# Run all tests
dotnet test

# Run tests for a specific project
dotnet test src/Chrysalis.Cbor.Test/Chrysalis.Test.csproj

# Run a specific test or test class
dotnet test --filter "FullyQualifiedName=Chrysalis.Cbor.Test.SomeTestClass"

# Run tests with coverage
dotnet test /p:CollectCoverage=true
```

## Benchmark Commands

```bash
# Run benchmarks
dotnet run -c Release --project src/Chrysalis.Cbor.Benchmark/Chrysalis.Cbor.Benchmark.csproj
```

## Plutus Native Library Build

The project contains Rust components in the Plutus module that need to be built:

```bash
# Build Rust libraries
cd src/Chrysalis.Plutus
./build-rs.sh
```

The `build-rs.sh` script will automatically build the appropriate library for your platform (Linux or macOS).

## Project Structure and Architecture

Chrysalis is organized into several modules:

1. **Chrysalis.Cbor**: Core CBOR serialization/deserialization functionality
   - Includes extensions and types for Cardano blockchain structures
   - Handles the (de)serialization of Cardano data types

2. **Chrysalis.Cbor.CodeGen**: Code generation for serialization/deserialization
   - Generates optimized serialization code for CBOR types
   - Supports attributes for customizing serialization behavior

3. **Chrysalis.Network**: Networking functionality for Cardano node interaction
   - Implementation of Cardano mini-protocols
   - Support for different bearers (TCP, Unix sockets)
   - Multiplexer for handling multiple protocol connections

4. **Chrysalis.Wallet**: Wallet-related functionality
   - Address generation and management
   - Key handling (private keys, public keys)
   - Mnemonic phrase support

5. **Chrysalis.Tx**: Transaction building and submission
   - Transaction construction
   - Fee calculation and coin selection utilities
   - Transaction parameter management

6. **Chrysalis.Plutus**: Plutus script evaluation
   - Integration with Plutus VM (via Rust FFI)
   - Script evaluation in transaction contexts

Each module has corresponding CLI projects for command-line interaction and testing.

## Key Concepts

1. **CBOR Serialization**: The library provides serialization and deserialization of Cardano data structures using CBOR format, with optimizations for performance and memory efficiency.

2. **Extensibility**: The codebase uses extension methods extensively to provide convenient access to nested data structures while maintaining clean type definitions.

3. **Cross-Platform Compatibility**: The library is designed to work across different platforms, with special handling for platform-specific components (like the Plutus VM native libraries).

4. **Attribute-Based Serialization**: The serialization framework uses C# attributes to control how objects are serialized to and from CBOR format, combined with source generators for better performance.

5. **Cardano Protocol Compatibility**: The library implements Cardano mini-protocols for node communication and follows the Cardano CDDL specification.

6. **Template-Based Transaction Building**: Advanced transaction building capabilities that use templates to simplify common transaction patterns.

7. **CIP Compliance**: Implementation of Cardano Improvement Proposals (CIPs) for standardized functionality.

## Package Publication

The main project is published as a NuGet package that includes all the necessary components:

```bash
# Pack the NuGet package
dotnet pack -c Release

# Publish to NuGet (requires API key)
dotnet nuget push bin/Release/Chrysalis.*.nupkg -k [API_KEY] -s https://api.nuget.org/v3/index.json
```

## Cardano Era Support

Chrysalis currently supports the following Cardano eras:

| Era | Phase | Status |
|-----|-------|--------|
| **Byron** | Foundation | Planned for future releases |
| **Shelley** (+ Allegra, Mary) | Decentralization | Fully supported |
| **Alonzo** (Goguen) | Smart Contracts | Fully supported |
| **Babbage/Vasil** (Basho) | Scaling | Fully supported |
| **Conway** (Voltaire) | Governance | Fully supported |

## Best Practices for Development

When working with Chrysalis, consider these best practices:

1. **Use Attributes Correctly**: Follow the established attribute patterns for CBOR serialization (`[CborSerializable]`, `[CborProperty]`, etc.)

2. **Leverage Extension Methods**: Access nested data using provided extension methods instead of direct access to maintain cleaner code.

3. **Handle Native Libraries**: Be mindful of platform-specific concerns when working with Plutus components that require native library integration.

4. **Check CIP Specifications**: When implementing wallet or transaction functionality, refer to the relevant Cardano Improvement Proposals (CIPs) for standards compliance.

5. **Use Template Builders**: For complex transactions, prefer the template-based builders over manual construction when possible.

6. **Performance Considerations**: Be aware of memory usage patterns, especially when processing large blocks or many transactions.

## Git Workflow Guidelines

When working with this repository:

1. **Avoid Force Pushing**: As a general rule, avoid using `git push --force` or `git push -f` as this can overwrite others' work and destroy history. Only use force pushing in exceptional circumstances:
   - On your own personal feature branches that no one else is working on
   - When absolutely necessary to resolve complex merge conflicts
   - Never force push to shared branches, especially `main`

2. **Use Pull Requests**: All changes should be made through pull requests, not direct commits to main.

3. **Follow Conventional Commits**: Use the conventional commits format (`feat:`, `fix:`, `docs:`, etc.) for clear commit messages.

4. **Create Topic Branches**: Always work in feature branches (e.g., `feature/new-feature` or `fix/bug-fix`) rather than directly on main.

5. **Keep PRs Focused**: Each pull request should address a single concern or feature to make review easier.

## Performance Benchmarking

Chrysalis has demonstrated excellent performance in benchmarks against similar libraries in other languages (including Rust-based implementations). When working on performance-critical code:

1. **Use BenchmarkDotNet**: The project includes benchmark configurations using BenchmarkDotNet to accurately measure performance.

2. **Compare with Previous Versions**: When making significant changes, run benchmarks against previous Chrysalis versions to ensure no performance regressions.

3. **Common Benchmarking Scenarios**:
   - Block deserialization
   - Chain synchronization
   - Transaction building
   - Database operations for blockchain data

4. **Run Benchmark Command**:
   ```bash
   dotnet run -c Release --project src/Chrysalis.Cbor.Benchmark/Chrysalis.Cbor.Benchmark.csproj
   ```