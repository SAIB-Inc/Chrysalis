# Chain-Sync Protocol Implementation

## Executive Summary

This document analyzes the conformance of Chrysalis's Chain-Sync implementation against the official Ouroboros network specification and provides a roadmap for completing the N2C (Node-to-Client) implementation based on the current codebase.

## Conformance Overview

| Feature | Status | Notes |
|---------|--------|-------|
| Message Types | ✅ Complete | All 8 messages implemented (including MsgDone) |
| CDDL Encoding | ✅ Compliant | Correct array format and tags |
| State Machine | ❌ Missing | No explicit state tracking |
| Timeouts | ❌ Missing | No timeout implementation |
| Size Limits | ✅ Compliant | Respects 65KB limit |
| Protocol Numbers | ✅ Compliant | Correct N2N/N2C numbers |
| N2N Support | ❌ Missing | Only N2C implemented |

## Detailed Analysis

### 1. Message Types Conformance

#### Specification Requirements

The Chain-Sync protocol defines 8 message types:

```cddl
chainSyncMessage
    = msgRequestNext        ; [0]
    / msgAwaitReply         ; [1]
    / msgRollForward        ; [2, header, tip]
    / msgRollBackward       ; [3, point, tip]
    / msgFindIntersect      ; [4, points]
    / msgIntersectFound     ; [5, point, tip]
    / msgIntersectNotFound  ; [6, tip]
    / chainSyncMsgDone      ; [7]
```

#### Chrysalis Implementation

```csharp
[CborSerializable]
[CborUnion]
public abstract partial record ChainSyncMessage : CborBase;

[CborSerializable]
[CborList]
public partial record MessageNextRequest(
    [CborOrder(0)] Value0 Idx
) : ChainSyncMessage;

// ... other messages implemented similarly
```

**✅ Correctly Implemented:**
- Array-based encoding with tag as first element
- Proper field ordering
- Correct tag values (0-7)
- Type-safe discriminated union pattern
- All 8 message types including `MsgDone`

### 2. State Machine Conformance

#### Specification Requirements

The protocol defines 5 states with clear agency rules:

| State | Agency | Description |
|-------|--------|-------------|
| StIdle | Client | Waiting to send request |
| StCanAwait | Server | Can send await or data |
| StMustReply | Server | Must send data |
| StIntersect | Server | Finding intersection |
| StDone | Nobody | Protocol terminated |

#### Chrysalis Implementation

**❌ No State Machine:**
- No explicit state tracking
- No agency enforcement
- Protocol flow managed implicitly
- Client can call methods in any order

**Example of Missing State Logic:**
```csharp
// Current implementation - no state validation
public async Task<ChainSyncMessage> SendRequestAsync()
{
    await _buffer.SendMessageAsync(new MessageNextRequest());
    return await _buffer.ReceiveMessageAsync<ChainSyncMessage>();
}

// Should have state validation like:
public async Task<ChainSyncMessage> SendRequestAsync()
{
    if (_state != ChainSyncState.StIdle)
        throw new ProtocolViolationException($"Cannot send request in state {_state}");
    
    _state = ChainSyncState.StCanAwait;
    // ... rest of implementation
}
```

### 3. Timeout Conformance

#### Specification Requirements

| State | Timeout | Description |
|-------|---------|-------------|
| StIdle | 3673s | ~1 hour timeout for idle connections |
| StCanAwait | 10s | Quick timeout when awaiting |
| StMustReply | 135-269s | Random timeout to prevent synchronization |
| StIntersect | 10s | Quick timeout for intersection finding |

#### Chrysalis Implementation

**❌ No Timeouts Implemented:**
- No timeout configuration
- No timeout enforcement
- No connection teardown on timeout violation

### 4. Size Limits Conformance

#### Specification Requirements
- Maximum 65,535 bytes per message in any state
- Connection should be torn down on violation

#### Chrysalis Implementation

**✅ Size Limits Respected:**
```csharp
public const int MaxSegmentPayloadLength = ushort.MaxValue; // 65,535 bytes
```

- Proper fragmentation in `ChannelBuffer`
- Respects protocol-level size constraints

### 5. Protocol Numbers Conformance

#### Specification Requirements
- Node-to-Node: Protocol ID 2
- Node-to-Client: Protocol ID 5

#### Chrysalis Implementation

**✅ Correct Protocol Numbers:**
```csharp
public enum ProtocolType : ushort
{
    NodeChainSync = 2,      // N2N
    ClientChainSync = 5,    // N2C
}
```

### 6. N2N vs N2C Support

#### Specification Requirements

**Node-to-Node (N2N):**
- Transfers block headers only
- Used with Block-Fetch for full blocks
- Server and client roles

**Node-to-Client (N2C):**
- Transfers full blocks
- Client-only role
- No size limits or timeouts

#### Chrysalis Implementation

**❌ Limited to N2C:**
- Only implements client-side protocol
- No server-side implementation
- No distinction between header-only and full blocks
- Uses `ClientChainSync = 5` protocol number

### 7. Additional Protocol Features

#### Missing Features

1. **Pipelining Support:**
   - Specification supports multiple in-flight requests
   - Not implemented in Chrysalis

2. **Read Pointer Management:**
   - Server-side state tracking per client
   - Not applicable to current client-only implementation

3. **Fork Handling:**
   - Proper rollback to common ancestor
   - Partially implemented (messages exist but no state logic)

## Recommendations for Full Conformance

### 1. Implement Complete Message Set
```csharp
[CborSerializable]
[CborList]
public partial record MessageDone(
    [CborOrder(0)] Value7 Idx
) : ChainSyncMessage;
```

### 2. Add State Machine
```csharp
public class ChainSyncProtocol
{
    private ChainSyncState _state = ChainSyncState.StIdle;
    private readonly Dictionary<ChainSyncState, HashSet<Type>> _validTransitions;
    
    private void ValidateTransition(ChainSyncMessage message)
    {
        if (!_validTransitions[_state].Contains(message.GetType()))
            throw new ProtocolViolationException($"Invalid message {message.GetType()} in state {_state}");
    }
}
```

### 3. Implement Timeouts
```csharp
public class ChainSyncTimeouts
{
    public TimeSpan StIdle { get; set; } = TimeSpan.FromSeconds(3673);
    public TimeSpan StCanAwait { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan StMustReply { get; set; } = TimeSpan.FromSeconds(
        Random.Shared.Next(135, 270));
    public TimeSpan StIntersect { get; set; } = TimeSpan.FromSeconds(10);
}
```

### 4. Add N2N Support
- Implement server-side chain sync
- Distinguish between header-only and full block modes
- Add producer-side state management

### 5. Enhanced Error Handling
- Protocol violation exceptions
- Automatic state recovery
- Connection teardown on violations

## Impact Assessment

### Current Functionality
- ✅ Basic blockchain synchronization works
- ✅ Can download blocks from a node
- ✅ Proper CBOR encoding/decoding

### Limitations
- ❌ No protection against malicious/buggy nodes
- ❌ No timeout-based connection management
- ❌ Cannot act as a chain producer
- ❌ Missing protocol termination capability

### Risk Assessment
- **Low Risk:** For trusted node connections
- **Medium Risk:** For public node connections without timeouts
- **High Risk:** For production node implementations

## N2C Implementation Plan

### Current State

The Chrysalis Chain-Sync implementation currently provides:
- ✅ All 8 message types with correct CBOR encoding
- ✅ Client-side protocol implementation (consumer role)
- ✅ Async/await pattern for non-blocking operations
- ✅ Integration with NodeClient for easy usage

### Phase 1: State Machine Implementation

**Goal**: Add explicit state tracking to ensure protocol correctness

```csharp
public enum ChainSyncState
{
    StIdle,      // Client can send requests
    StCanAwait,  // Server can send await or data
    StMustReply, // Server must send data
    StIntersect, // Server finding intersection
    StDone       // Protocol terminated
}

public class ChainSyncStateMachine
{
    private ChainSyncState _state = ChainSyncState.StIdle;
    
    public void ValidateTransition(Type messageType)
    {
        // Implement state transition validation
    }
}
```

**Tasks**:
1. Create state enum and state machine class
2. Add state validation to all message sends/receives
3. Implement proper agency tracking
4. Add state transition logging for debugging

### Phase 2: Timeout Implementation

**Goal**: Implement timeouts as per specification for N2C

```csharp
public class ChainSyncTimeoutConfig
{
    // N2C has no timeouts per spec, but we should add configurable ones
    public TimeSpan? MessageTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan? IdleTimeout { get; set; } = null; // No idle timeout for N2C
}
```

**Tasks**:
1. Add timeout configuration
2. Implement cancellation token timeout wrappers
3. Add timeout handling and connection teardown
4. Make timeouts configurable per deployment

### Phase 3: Enhanced Error Handling

**Goal**: Robust error handling and recovery

```csharp
public class ChainSyncException : Exception
{
    public ChainSyncState State { get; }
    public Type? MessageType { get; }
}

public class ProtocolViolationException : ChainSyncException
{
    // Protocol-specific violations
}
```

**Tasks**:
1. Create exception hierarchy
2. Add try-catch blocks with proper error propagation
3. Implement connection recovery strategies
4. Add detailed error logging

### Phase 4: Pipelining Support

**Goal**: Improve performance with pipelined requests

```csharp
public class PipelinedChainSync : ChainSync
{
    private readonly int _maxPipelineDepth;
    private readonly Queue<TaskCompletionSource<MessageNextResponse>> _pendingResponses;
    
    public async Task<MessageNextResponse[]> NextRequestBatchAsync(int count)
    {
        // Send multiple requests without waiting
    }
}
```

**Tasks**:
1. Implement request queue management
2. Add response correlation
3. Handle out-of-order responses
4. Add pipeline depth configuration

### Phase 5: Testing and Documentation

**Goal**: Comprehensive testing and usage documentation

**Tasks**:
1. Unit tests for all message types
2. Integration tests with mock server
3. State machine property-based tests
4. Performance benchmarks
5. Usage examples and best practices

### Implementation Priority

1. **High Priority** (Production readiness):
   - State machine implementation
   - Basic timeout support
   - Enhanced error handling

2. **Medium Priority** (Performance):
   - Pipelining support
   - Connection pooling
   - Metrics collection

3. **Low Priority** (Nice to have):
   - Advanced recovery strategies
   - Diagnostic tools
   - Protocol analyzers

### Usage Example (After Implementation)

```csharp
// Create client with configuration
var config = new ChainSyncConfig
{
    EnableStateValidation = true,
    MessageTimeout = TimeSpan.FromSeconds(30),
    MaxPipelineDepth = 10
};

var client = await NodeClient.ConnectAsync("/tmp/node.socket", config);
await client.StartAsync();

// Use with state tracking
var chainSync = client.ChainSync;

// Find intersection with state validation
var intersection = await chainSync.FindIntersectionAsync(points);

// Pipeline multiple requests
var responses = await chainSync.NextRequestBatchAsync(5);

// Graceful shutdown
await chainSync.DoneAsync();
```

## Conclusion

The Chrysalis Chain-Sync implementation has a solid foundation with correct message types and CBOR encoding. The implementation plan focuses on adding the missing protocol-level features while maintaining the clean API design. By following this phased approach, we can achieve full N2C compliance while maintaining backward compatibility.