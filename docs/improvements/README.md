# Chrysalis Improvement Roadmap

This directory contains component-specific improvement roadmaps for the Chrysalis project, organized by module. These documents outline the strategic direction for transforming Chrysalis from a client library into a full block-producing Cardano node with pure .NET implementation.

## Roadmap Structure

Each component has its own improvement document with the following structure:
- **Overview** - Current state analysis and transformation goals
- **Priority-Based Improvements** - Categorized enhancements with clear priorities  
- **Implementation Strategy** - Phased approach and integration requirements
- **Success Metrics** - Measurable outcomes and performance targets

## Components

| Component | Focus Areas | Priority |
|-----------|------------|----------|
| [CBOR](./cbor-improvements.md) | Raw data invalidation, performance optimization, serialization enhancements | High |
| [Transaction](./transaction-improvements.md) | Mempool-aware building, advanced coin selection, seamless UTXO management | High |
| [Wallet](./wallet-improvements.md) | Hot/Cold architecture, hardware wallet integration, CIP compliance | High |
| [Network](./network-improvements.md) | State machine implementation, Node-to-Node protocols, block production support | Critical |

## Strategic Objectives

### Primary Goals
- **Full Node Capability**: Transform from client library to block-producing node
- **Pure .NET Implementation**: Eliminate FFI dependencies for cross-platform compatibility
- **Protocol Completeness**: Implement all Ouroboros mini-protocols with proper state machines
- **Enterprise Readiness**: Production-grade reliability, performance, and observability

### Architecture Principles
- **State Machine First**: Proper protocol state management with error handling
- **Hot/Cold Security Model**: Hardware wallet integration as cold wallet type
- **Mempool Awareness**: Transaction building with UTXO conflict detection
- **Cross-Platform Native**: Single .NET assembly deployment

## Implementation Approach

### Phased Development
1. **Foundation Phase**: Core infrastructure and state machine framework
2. **Protocol Phase**: Complete mini-protocol implementation
3. **Integration Phase**: Cross-module integration and block production
4. **Optimization Phase**: Performance tuning and enterprise features

### Quality Standards
- Comprehensive unit and integration testing
- Performance benchmarking against reference implementations
- Cardano specification compliance validation
- Security audit preparation