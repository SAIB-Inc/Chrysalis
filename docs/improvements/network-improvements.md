# Network Module Improvement Roadmap

## From Simple Client to Full Block-Producing Cardano Node

## Current State Assessment

### Current Protocol Implementation Status

#### ✅ **Implemented but Incomplete Protocols (Node-to-Client)**

**1. ChainSync Protocol** - HAS partial state machine

- ✅ **State Enum**: Proper `ChainSyncState` (Idle, CanAwait, MustReply, Intersect, Done)
- ✅ **State Tracking**: Basic state transitions in happy path
- ❌ **Error Handling**: No error states or protocol violation recovery
- ❌ **Complete Validation**: Doesn't validate all invalid state transitions

**2. LocalStateQuery Protocol** - MINIMAL state tracking

- ❌ **No State Machine**: Only `bool _isAcquired = false`
- ❌ **No Error States**: Assumes acquire will always work
- ❌ **No Protocol Validation**: Missing states like Acquiring, Querying, Failed

**3. LocalTxSubmit Protocol** - NO state machine

- ❌ **No State Tracking**: Just send/receive pattern
- ❌ **No Error Handling**: No failed submission states
- ❌ **No Protocol Compliance**: Simple request-response only

**4. LocalTxMonitor Protocol** - NO state machine

- ❌ **No State Tracking**: Direct message operations
- ❌ **Incomplete Implementation**: Has @TODO comments
- ❌ **No Session Management**: No proper acquire/release state tracking

#### ❌ **Missing Protocols (Node-to-Node)**

From `ProtocolType.cs` enum, these are **defined but completely unimplemented**:

- **NodeChainSync** (ProtocolType = 2)
- **BlockFetch** (ProtocolType = 3)
- **TxSubmission** (ProtocolType = 4)
- **KeepAlive** (ProtocolType = 8)
- **PeerSharing** (ProtocolType = 10)

### Architectural Issues

#### ❌ **No Generic State Machine Framework**

- Each protocol implements state handling differently
- No validation of state transitions
- No common error handling patterns
- No timeout or failure recovery mechanisms

#### ❌ **Optimistic Implementation**

- Current code assumes "everything will work perfectly"
- No handling of network failures, timeouts, or protocol violations
- Missing proper error states and recovery mechanisms

---

## Priority 1: Complete State Machine Framework for All Protocols

**Priority**: P1 (Critical)  
**Impact**: Foundation for reliable protocol implementation, error handling, proper Ouroboros compliance

### Problem Statement

Current implementations lack proper state machine foundations, leading to:

- **Fragile network operations** that break on any error condition
- **Protocol violations** when unexpected messages arrive
- **No timeout handling** or connection recovery
- **Inconsistent error handling** across different protocols

### Generic State Machine Infrastructure

#### Base State Machine Framework

```csharp
public abstract class ProtocolStateMachine<TState, TMessage> : IMiniProtocol
    where TState : Enum
    where TMessage : class
{
    protected readonly ChannelBuffer _buffer;
    protected TState _currentState;
    protected readonly ILogger _logger;

    protected ProtocolStateMachine(AgentChannel channel, TState initialState, ILogger logger)
    {
        _buffer = new ChannelBuffer(channel);
        _currentState = initialState;
        _logger = logger;
    }

    public TState CurrentState => _currentState;
    public abstract bool HasAgency { get; }
    public abstract bool IsTerminalState { get; }

    // Core state machine operations
    protected async Task<TMessage> SendAndReceiveAsync<TRequest>(
        TRequest request,
        TState expectedNextState,
        CancellationToken cancellationToken) where TRequest : TMessage
    {
        ValidateCanSend(expectedNextState);

        await _buffer.SendFullMessageAsync(request, cancellationToken);
        TransitionTo(expectedNextState);

        var response = await _buffer.ReceiveFullMessageAsync<TMessage>(cancellationToken);
        ProcessResponse(response);

        return response;
    }

    protected void TransitionTo(TState newState)
    {
        if (!IsValidTransition(_currentState, newState))
        {
            var error = $"Invalid state transition from {_currentState} to {newState}";
            _logger.LogError(error);
            throw new ProtocolViolationException(error);
        }

        _logger.LogDebug("Protocol state transition: {From} -> {To}", _currentState, newState);
        _currentState = newState;
    }

    // Abstract methods that each protocol must implement
    protected abstract bool IsValidTransition(TState fromState, TState toState);
    protected abstract void ProcessResponse(TMessage response);
    protected abstract void ValidateCanSend(TState intendedState);
}
```

### Complete ChainSync State Machine

```csharp
public enum ChainSyncState
{
    // Normal operation states
    Idle,           // Client has agency
    CanAwait,       // Awaiting server response
    MustReply,      // Server has agency
    Intersect,      // Finding intersection

    // Error states
    Failed,         // Protocol violation or network error
    Timeout,        // Operation timed out

    // Terminal states
    Done            // Protocol completed
}

public class ChainSync : ProtocolStateMachine<ChainSyncState, ChainSyncMessage>
{
    private readonly ChainSyncMessage _nextRequest = ChainSyncMessages.NextRequest();
    private readonly Timer? _timeoutTimer;
    private readonly TimeSpan _responseTimeout = TimeSpan.FromSeconds(30);

    public ChainSync(AgentChannel channel, ILogger<ChainSync> logger)
        : base(channel, ChainSyncState.Idle, logger) { }

    public override bool HasAgency => _currentState is ChainSyncState.Idle;
    public override bool IsTerminalState => _currentState is ChainSyncState.Done or ChainSyncState.Failed;

    // Proper intersection finding with timeout and error handling
    public async Task<ChainSyncMessage> FindIntersectionAsync(
        IEnumerable<Point> points,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timeoutCts = new CancellationTokenSource(_responseTimeout);
            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            var message = new Points([.. points]);
            var request = ChainSyncMessages.FindIntersect(message);

            return await SendAndReceiveAsync(request, ChainSyncState.Intersect, combinedCts.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TransitionTo(ChainSyncState.Failed);
            throw;
        }
        catch (OperationCanceledException)
        {
            TransitionTo(ChainSyncState.Timeout);
            throw new TimeoutException("Intersection request timed out");
        }
        catch (Exception ex)
        {
            TransitionTo(ChainSyncState.Failed);
            _logger.LogError(ex, "Failed to find intersection");
            throw;
        }
    }

    protected override bool IsValidTransition(ChainSyncState fromState, ChainSyncState toState)
    {
        return (fromState, toState) switch
        {
            // Valid normal transitions
            (ChainSyncState.Idle, ChainSyncState.CanAwait) => true,
            (ChainSyncState.Idle, ChainSyncState.Intersect) => true,
            (ChainSyncState.CanAwait, ChainSyncState.MustReply) => true,
            (ChainSyncState.CanAwait, ChainSyncState.Idle) => true,
            (ChainSyncState.MustReply, ChainSyncState.Idle) => true,
            (ChainSyncState.Intersect, ChainSyncState.Idle) => true,

            // Valid terminal transitions
            (ChainSyncState.Idle, ChainSyncState.Done) => true,

            // Error transitions (from any state)
            (_, ChainSyncState.Failed) => true,
            (_, ChainSyncState.Timeout) => true,

            // Invalid transitions
            _ => false
        };
    }

    protected override void ProcessResponse(ChainSyncMessage response)
    {
        switch (response)
        {
            case MessageAwaitReply:
                TransitionTo(ChainSyncState.MustReply);
                break;
            case MessageRollForward:
                TransitionTo(ChainSyncState.Idle);
                break;
            case MessageRollBackward:
                TransitionTo(ChainSyncState.Idle);
                break;
            case MessageIntersectFound:
                TransitionTo(ChainSyncState.Idle);
                break;
            case MessageIntersectNotFound:
                TransitionTo(ChainSyncState.Idle);
                break;
            default:
                var error = $"Unexpected response in state {_currentState}: {response}";
                _logger.LogError(error);
                throw new ProtocolViolationException(error);
        }
    }

    protected override void ValidateCanSend(ChainSyncState intendedState)
    {
        if (!HasAgency && intendedState != ChainSyncState.Failed)
        {
            throw new InvalidOperationException($"Cannot send message in state {_currentState} - server has agency");
        }
    }
}
```

### Complete LocalStateQuery State Machine

```csharp
public enum LocalStateQueryState
{
    // Normal operation states
    Idle,           // Not acquired, can acquire
    Acquiring,      // Sent acquire, awaiting response
    Acquired,       // State acquired, can query or re-acquire
    Querying,       // Sent query, awaiting result
    Releasing,      // Sent release, awaiting confirmation

    // Error states
    AcquireFailed,  // Acquire failed
    QueryFailed,    // Query failed

    // Terminal states
    Released,       // Successfully released
    Failed          // Protocol error
}

public class LocalStateQuery : ProtocolStateMachine<LocalStateQueryState, LocalStateQueryMessage>
{
    private Point? _currentPoint;

    public LocalStateQuery(AgentChannel channel, ILogger<LocalStateQuery> logger)
        : base(channel, LocalStateQueryState.Idle, logger) { }

    public override bool HasAgency => _currentState is
        LocalStateQueryState.Idle or
        LocalStateQueryState.Acquired;

    public override bool IsTerminalState => _currentState is
        LocalStateQueryState.Released or
        LocalStateQueryState.Failed;

    public async Task<LocalStateQueryMessage> AcquireAsync(Point? point, CancellationToken cancellationToken)
    {
        try
        {
            var request = AcquireTypes.Default(point);
            var response = await SendAndReceiveAsync(request, LocalStateQueryState.Acquiring, cancellationToken);

            if (response is Acquired)
            {
                _currentPoint = point;
                TransitionTo(LocalStateQueryState.Acquired);
                _logger.LogDebug("State acquired at point {Point}", point);
            }
            else if (response is AcquireFailure failure)
            {
                TransitionTo(LocalStateQueryState.AcquireFailed);
                throw new StateAcquireException($"Failed to acquire state: {failure.Reason}");
            }

            return response;
        }
        catch (Exception ex) when (!(ex is StateAcquireException))
        {
            TransitionTo(LocalStateQueryState.Failed);
            _logger.LogError(ex, "Acquire operation failed");
            throw;
        }
    }

    public async Task<Result> QueryAsync(QueryReq query, CancellationToken cancellationToken)
    {
        if (_currentState != LocalStateQueryState.Acquired)
            throw new InvalidOperationException($"Cannot query in state {_currentState}. Must acquire state first.");

        try
        {
            var request = QueryRequest.New(query);
            var response = await SendAndReceiveAsync(request, LocalStateQueryState.Querying, cancellationToken);

            TransitionTo(LocalStateQueryState.Acquired); // Back to acquired after query

            return response switch
            {
                Result result => result,
                _ => throw new ProtocolViolationException($"Unexpected query response: {response}")
            };
        }
        catch (Exception ex)
        {
            TransitionTo(LocalStateQueryState.QueryFailed);
            _logger.LogError(ex, "Query failed: {Query}", query);
            throw;
        }
    }

    protected override bool IsValidTransition(LocalStateQueryState fromState, LocalStateQueryState toState)
    {
        return (fromState, toState) switch
        {
            // Normal operation transitions
            (LocalStateQueryState.Idle, LocalStateQueryState.Acquiring) => true,
            (LocalStateQueryState.Acquiring, LocalStateQueryState.Acquired) => true,
            (LocalStateQueryState.Acquiring, LocalStateQueryState.AcquireFailed) => true,
            (LocalStateQueryState.Acquired, LocalStateQueryState.Querying) => true,
            (LocalStateQueryState.Acquired, LocalStateQueryState.Acquiring) => true, // Re-acquire
            (LocalStateQueryState.Acquired, LocalStateQueryState.Releasing) => true,
            (LocalStateQueryState.Querying, LocalStateQueryState.Acquired) => true,
            (LocalStateQueryState.Querying, LocalStateQueryState.QueryFailed) => true,
            (LocalStateQueryState.Releasing, LocalStateQueryState.Released) => true,

            // Error recovery
            (LocalStateQueryState.AcquireFailed, LocalStateQueryState.Acquiring) => true,
            (LocalStateQueryState.QueryFailed, LocalStateQueryState.Acquiring) => true,

            // Terminal transitions
            (_, LocalStateQueryState.Failed) => true,

            _ => false
        };
    }

    protected override void ProcessResponse(LocalStateQueryMessage response)
    {
        // Response processing handled in specific methods
        // State transitions are managed by the calling methods
    }

    protected override void ValidateCanSend(LocalStateQueryState intendedState)
    {
        if (!HasAgency)
            throw new InvalidOperationException($"Cannot send message in state {_currentState}");
    }
}
```

### Complete LocalTxSubmit State Machine

```csharp
public enum LocalTxSubmitState
{
    // Normal states
    Idle,           // Ready to submit
    Submitting,     // Transaction submitted, awaiting response

    // Result states
    Accepted,       // Transaction accepted
    Rejected,       // Transaction rejected

    // Error states
    Failed,         // Network or protocol error

    // Can submit again after any result
}

public class LocalTxSubmit : ProtocolStateMachine<LocalTxSubmitState, LocalTxSubmissionMessage>
{
    public LocalTxSubmit(AgentChannel channel, ILogger<LocalTxSubmit> logger)
        : base(channel, LocalTxSubmitState.Idle, logger) { }

    public override bool HasAgency => _currentState is LocalTxSubmitState.Idle;
    public override bool IsTerminalState => _currentState is LocalTxSubmitState.Failed;

    public async Task<TxSubmissionResult> SubmitTxAsync(SubmitTx submit, CancellationToken cancellationToken)
    {
        try
        {
            var response = await SendAndReceiveAsync(submit, LocalTxSubmitState.Submitting, cancellationToken);

            return response switch
            {
                AcceptTx accept =>
                {
                    TransitionTo(LocalTxSubmitState.Accepted);
                    new TxSubmissionResult(true, null);
                },
                RejectTx reject =>
                {
                    TransitionTo(LocalTxSubmitState.Rejected);
                    new TxSubmissionResult(false, reject.Reason);
                },
                _ => throw new ProtocolViolationException($"Unexpected submission response: {response}")
            };
        }
        catch (Exception ex)
        {
            TransitionTo(LocalTxSubmitState.Failed);
            _logger.LogError(ex, "Transaction submission failed");
            throw;
        }
        finally
        {
            // Reset to idle for next submission (unless failed)
            if (_currentState != LocalTxSubmitState.Failed)
            {
                TransitionTo(LocalTxSubmitState.Idle);
            }
        }
    }

    protected override bool IsValidTransition(LocalTxSubmitState fromState, LocalTxSubmitState toState)
    {
        return (fromState, toState) switch
        {
            (LocalTxSubmitState.Idle, LocalTxSubmitState.Submitting) => true,
            (LocalTxSubmitState.Submitting, LocalTxSubmitState.Accepted) => true,
            (LocalTxSubmitState.Submitting, LocalTxSubmitState.Rejected) => true,
            (LocalTxSubmitState.Accepted, LocalTxSubmitState.Idle) => true,
            (LocalTxSubmitState.Rejected, LocalTxSubmitState.Idle) => true,
            (_, LocalTxSubmitState.Failed) => true,
            _ => false
        };
    }

    protected override void ProcessResponse(LocalTxSubmissionMessage response)
    {
        // Handled in SubmitTxAsync method
    }

    protected override void ValidateCanSend(LocalTxSubmitState intendedState)
    {
        if (!HasAgency)
            throw new InvalidOperationException($"Cannot submit in state {_currentState}");
    }
}

public record TxSubmissionResult(bool Accepted, string? RejectionReason);
```

### Complete LocalTxMonitor State Machine

```csharp
public enum LocalTxMonitorState
{
    // Session states
    Idle,           // Not monitoring
    Acquiring,      // Acquiring mempool snapshot
    Acquired,       // Monitoring active
    Querying,       // Executing query
    Releasing,      // Releasing snapshot

    // Error states
    AcquireFailed,
    QueryFailed,

    // Terminal states
    Released,
    Failed
}

public class LocalTxMonitor : ProtocolStateMachine<LocalTxMonitorState, LocalTxMonitorMessage>
{
    public LocalTxMonitor(AgentChannel channel, ILogger<LocalTxMonitor> logger)
        : base(channel, LocalTxMonitorState.Idle, logger) { }

    public override bool HasAgency => _currentState is
        LocalTxMonitorState.Idle or
        LocalTxMonitorState.Acquired;

    public override bool IsTerminalState => _currentState is
        LocalTxMonitorState.Released or
        LocalTxMonitorState.Failed;

    public async Task AcquireAsync(CancellationToken cancellationToken)
    {
        var acquire = LocalTxMonitorMessages.Acquire();
        var response = await SendAndReceiveAsync(acquire, LocalTxMonitorState.Acquiring, cancellationToken);

        if (response is Acquired)
        {
            TransitionTo(LocalTxMonitorState.Acquired);
        }
        else
        {
            TransitionTo(LocalTxMonitorState.AcquireFailed);
            throw new MemPoolAcquireException($"Failed to acquire mempool: {response}");
        }
    }

    public async Task<MempoolTransaction> NextTxAsync(CancellationToken cancellationToken)
    {
        if (_currentState != LocalTxMonitorState.Acquired)
            throw new InvalidOperationException("Must acquire mempool before requesting transactions");

        var nextTx = LocalTxMonitorMessages.NextTx();
        var response = await SendAndReceiveAsync(nextTx, LocalTxMonitorState.Querying, cancellationToken);

        TransitionTo(LocalTxMonitorState.Acquired); // Back to acquired

        return response switch
        {
            ReplyNextTx reply => reply.Transaction,
            MustReply => throw new NotImplementedException("MustReply handling not implemented"),
            _ => throw new ProtocolViolationException($"Unexpected NextTx response: {response}")
        };
    }

    protected override bool IsValidTransition(LocalTxMonitorState fromState, LocalTxMonitorState toState)
    {
        return (fromState, toState) switch
        {
            (LocalTxMonitorState.Idle, LocalTxMonitorState.Acquiring) => true,
            (LocalTxMonitorState.Acquiring, LocalTxMonitorState.Acquired) => true,
            (LocalTxMonitorState.Acquiring, LocalTxMonitorState.AcquireFailed) => true,
            (LocalTxMonitorState.Acquired, LocalTxMonitorState.Querying) => true,
            (LocalTxMonitorState.Acquired, LocalTxMonitorState.Releasing) => true,
            (LocalTxMonitorState.Querying, LocalTxMonitorState.Acquired) => true,
            (LocalTxMonitorState.Querying, LocalTxMonitorState.QueryFailed) => true,
            (LocalTxMonitorState.Releasing, LocalTxMonitorState.Released) => true,
            (_, LocalTxMonitorState.Failed) => true,
            _ => false
        };
    }

    // ... implement remaining methods
}
```

### Benefits of Proper State Machines

- **Robust Error Handling**: All protocols can handle failures gracefully
- **Protocol Compliance**: Proper validation prevents protocol violations
- **Debugging**: Clear state transitions make issues easier to diagnose
- **Reliability**: Networks errors don't crash the entire connection
- **Consistency**: All protocols follow the same state management patterns

---

## Priority 2: Node-to-Node Protocol Implementation

**Priority**: P2 (High)  
**Impact**: Required for peer networking and full node capability

### Missing Node-to-Node Protocols

After implementing proper state machines for existing protocols, the missing N2N protocols need to be implemented:

1. **NodeChainSync** - Different from client ChainSync, for peer synchronization
2. **BlockFetch** - Download block bodies from peers
3. **TxSubmission** - Bidirectional transaction sharing between peers
4. **KeepAlive** - Connection maintenance between peers
5. **PeerSharing** - Discover and share peer addresses

Each would be implemented using the same state machine framework established above.

---

## Success Metrics

### State Machine Implementation

- **Protocol Compliance**: All protocols pass Ouroboros conformance tests
- **Error Handling**: 100% of network errors handled gracefully without crashing
- **State Validation**: Zero invalid state transitions in production
- **Recovery**: Automatic recovery from transient network failures

### Reliability Improvements

- **Connection Stability**: >99% uptime for established connections
- **Error Recovery**: <5 second recovery time from network interruptions
- **Protocol Violations**: Zero protocol violations in normal operation
- **Resource Management**: No memory leaks from failed protocol operations

---

## Implementation Strategy

### Phase 1: State Machine Foundation

- Implement generic `ProtocolStateMachine<TState, TMessage>` base class
- Rewrite existing ChainSync with complete state machine
- Add comprehensive error handling and timeout support

### Phase 2: Complete Existing Protocols

- Rewrite LocalStateQuery with proper state tracking
- Implement complete LocalTxSubmit state machine
- Fix LocalTxMonitor implementation with proper states

### Phase 3: Node-to-Node Protocols

- Implement the five missing N2N protocols using state machine framework
- Add connection management and peer discovery
- Create comprehensive testing suite

### Phase 4: Integration and Optimization

- Integrate all protocols with enhanced error handling
- Add monitoring and observability
- Performance optimization and production readiness
