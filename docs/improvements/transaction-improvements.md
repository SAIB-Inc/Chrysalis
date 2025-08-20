# Transaction Module Improvement Roadmap

## Current State Assessment

### Strengths
- ✅ **Dual Builder Pattern**: Both imperative (`TransactionBuilder`) and declarative (`TransactionTemplateBuilder`) approaches
- ✅ **Complete Era Support**: Full Conway era support including governance features
- ✅ **Performance Optimized**: Custom equality comparers, efficient asset handling
- ✅ **Provider Abstraction**: Supports Blockfrost, Kupo, and direct Ouroboros connections
- ✅ **Smart Contract Ready**: Comprehensive Plutus integration with redeemer support
- ✅ **Multi-Asset Support**: Native token and asset handling throughout

### Current Limitations
- ❌ **No Mempool Awareness**: Cannot detect consumed UTxOs or pending transactions
- ❌ **Complex Template API**: Template builder has become overly complex (1,137 lines)
- ❌ **Inconsistent Error Handling**: Generic exceptions with unclear error messages
- ❌ **Limited Testing**: No dedicated unit tests for core transaction building logic
- ❌ **Fee Calculation Issues**: Hardcoded constants and floating-point arithmetic for currency
- ❌ **Coin Selection Limitations**: Single algorithm, O(n²) complexity in asset matching
- ❌ **No Validation Framework**: Transaction validation only happens at build time

---

## Priority 1: Mempool-Aware Transaction Building

**Priority**: P1 (Critical)  
**Impact**: Eliminates UTXO double-spending errors, enables seamless transaction chaining, improves reliability

### Problem Statement

Current transaction building lacks mempool awareness, causing:
- **Double-spending errors** when multiple transactions try to consume the same UTXO
- **Stale UTXO errors** when building on already-consumed outputs
- **Transaction chaining failures** when trying to spend outputs from pending transactions
- **Poor developer experience** requiring manual UTXO tracking across transactions

### Technical Specification

#### 1. Mempool-Aware UTXO State Management
```csharp
public interface IMempoolAwareUtxoProvider : ICardanoDataProvider
{
    Task<UtxoSet> GetAvailableUtxosAsync(
        IEnumerable<string> addresses, 
        MempoolContext? context = null);
    
    Task<MempoolStatus> GetMempoolStatusAsync();
    
    Task<List<PendingTransaction>> GetPendingTransactionsAsync(
        IEnumerable<string> addresses);
}

public class MempoolContext
{
    public HashSet<TransactionInput> ConsumedUtxos { get; init; } = new();
    public Dictionary<TransactionInput, TransactionOutput> PendingUtxos { get; init; } = new();
    public List<string> TrackedTransactionIds { get; init; } = new();
    
    public MempoolContext WithConsumedUtxo(TransactionInput input)
    {
        var newConsumed = new HashSet<TransactionInput>(ConsumedUtxos) { input };
        return this with { ConsumedUtxos = newConsumed };
    }
    
    public MempoolContext WithPendingUtxo(TransactionInput input, TransactionOutput output)
    {
        var newPending = new Dictionary<TransactionInput, TransactionOutput>(PendingUtxos)
        {
            [input] = output
        };
        return this with { PendingUtxos = newPending };
    }
}
```

#### 2. Smart UTXO Selection with Mempool Filtering
```csharp
public class MempoolAwareCoinSelection
{
    private readonly IMempoolAwareUtxoProvider _provider;
    private readonly MempoolContext _context;
    
    public async Task<CoinSelectionResult> SelectCoinsAsync(
        IEnumerable<string> addresses,
        List<Value> requiredAmounts,
        CoinSelectionConstraints constraints)
    {
        // Get all UTxOs including pending ones
        var utxoSet = await _provider.GetAvailableUtxosAsync(addresses, _context);
        
        // Filter out consumed UTxOs from mempool
        var availableUtxos = FilterAvailableUtxos(utxoSet);
        
        // Include pending UTxOs that we can spend
        var spendableUtxos = IncludePendingUtxos(availableUtxos);
        
        return await SelectFromAvailableUtxos(spendableUtxos, requiredAmounts, constraints);
    }
    
    private List<ResolvedInput> FilterAvailableUtxos(UtxoSet utxoSet)
    {
        return utxoSet.Utxos
            .Where(utxo => !_context.ConsumedUtxos.Contains(utxo.Input))
            .Where(utxo => !IsConsumedInMempool(utxo.Input))
            .ToList();
    }
    
    private List<ResolvedInput> IncludePendingUtxos(List<ResolvedInput> baseUtxos)
    {
        var result = new List<ResolvedInput>(baseUtxos);
        
        foreach (var (input, output) in _context.PendingUtxos)
        {
            if (IsSpendable(input, output))
            {
                result.Add(new ResolvedInput(input, output, null));
            }
        }
        
        return result;
    }
}
```

#### 3. Transaction Chain Builder
```csharp
public class TransactionChainBuilder
{
    private readonly List<Transaction> _transactions = new();
    private readonly MempoolContext _context = new();
    private readonly IMempoolAwareUtxoProvider _provider;
    
    public TransactionChainBuilder AddTransaction(
        Func<MempoolContext, Task<Transaction>> transactionBuilder)
    {
        _transactionBuilders.Add(transactionBuilder);
        return this;
    }
    
    public async Task<TransactionChain> BuildChainAsync()
    {
        var context = _context;
        var transactions = new List<Transaction>();
        
        foreach (var builder in _transactionBuilders)
        {
            var tx = await builder(context);
            transactions.Add(tx);
            
            // Update context with this transaction's effects
            context = UpdateMempoolContext(context, tx);
        }
        
        return new TransactionChain(transactions);
    }
    
    private MempoolContext UpdateMempoolContext(MempoolContext context, Transaction tx)
    {
        var newContext = context;
        
        // Mark inputs as consumed
        foreach (var input in tx.TransactionBody.Inputs())
        {
            newContext = newContext.WithConsumedUtxo(input);
        }
        
        // Add outputs as pending UTxOs
        var outputs = tx.TransactionBody.Outputs().ToList();
        for (int i = 0; i < outputs.Count; i++)
        {
            var newInput = new TransactionInput(tx.GetHash(), (uint)i);
            newContext = newContext.WithPendingUtxo(newInput, outputs[i]);
        }
        
        return newContext;
    }
}
```

#### 4. Enhanced Provider Implementations
```csharp
public class MempoolAwareBlockfrostProvider : Blockfrost, IMempoolAwareUtxoProvider
{
    public async Task<UtxoSet> GetAvailableUtxosAsync(
        IEnumerable<string> addresses, 
        MempoolContext? context = null)
    {
        // Get base UTxOs from chain
        var chainUtxos = await base.GetUtxosAsync(addresses.ToList());
        
        // Get mempool transactions
        var mempoolTxs = await GetMempoolTransactionsAsync(addresses);
        
        // Filter out consumed UTxOs
        var availableUtxos = FilterConsumedUtxos(chainUtxos, mempoolTxs, context);
        
        // Add pending UTxOs from mempool
        var pendingUtxos = ExtractPendingUtxos(mempoolTxs, addresses);
        
        return new UtxoSet(
            ChainUtxos: availableUtxos,
            PendingUtxos: pendingUtxos,
            LastUpdated: DateTimeOffset.UtcNow);
    }
    
    public async Task<MempoolStatus> GetMempoolStatusAsync()
    {
        var response = await GetAsync("mempool");
        return response.Content.ReadFromJson<MempoolStatus>();
    }
    
    public async Task<List<PendingTransaction>> GetPendingTransactionsAsync(
        IEnumerable<string> addresses)
    {
        var tasks = addresses.Select(async addr =>
        {
            var response = await GetAsync($"addresses/{addr}/transactions?order=desc&count=50");
            var txs = await response.Content.ReadFromJson<List<BlockfrostTransaction>>();
            
            return txs?.Where(tx => tx.Block == null) // Unconfirmed transactions
                      .Select(MapToPendingTransaction)
                      .ToList() ?? new List<PendingTransaction>();
        });
        
        var results = await Task.WhenAll(tasks);
        return results.SelectMany(x => x).ToList();
    }
}
```

#### 5. Real-time Mempool Updates
```csharp
public class MempoolMonitor : IDisposable
{
    private readonly IMempoolAwareUtxoProvider _provider;
    private readonly Timer _refreshTimer;
    private readonly SemaphoreSlim _refreshSemaphore = new(1, 1);
    
    public event EventHandler<MempoolChangedEventArgs>? MempoolChanged;
    
    public async Task<MempoolSnapshot> GetCurrentSnapshotAsync()
    {
        await _refreshSemaphore.WaitAsync();
        try
        {
            var status = await _provider.GetMempoolStatusAsync();
            var pendingTxs = await _provider.GetPendingTransactionsAsync(_trackedAddresses);
            
            return new MempoolSnapshot(
                Timestamp: DateTimeOffset.UtcNow,
                TransactionCount: status.Count,
                PendingTransactions: pendingTxs,
                ConsumedUtxos: ExtractConsumedUtxos(pendingTxs));
        }
        finally
        {
            _refreshSemaphore.Release();
        }
    }
    
    public void StartMonitoring(TimeSpan refreshInterval = default)
    {
        var interval = refreshInterval == default ? TimeSpan.FromSeconds(30) : refreshInterval;
        _refreshTimer?.Change(TimeSpan.Zero, interval);
    }
}

public record MempoolSnapshot(
    DateTimeOffset Timestamp,
    int TransactionCount,
    List<PendingTransaction> PendingTransactions,
    HashSet<TransactionInput> ConsumedUtxos);
```

### Usage Examples

#### 1. Simple Mempool-Aware Transaction
```csharp
var provider = new MempoolAwareBlockfrostProvider("api_key");

var tx = await TransactionBuilder.Create(protocolParams)
    .WithMempoolProvider(provider)
    .AddInputFromAddress("addr1...")  // Automatically filters consumed UTxOs
    .AddOutput("addr_test1...", new Lovelace(5_000_000))
    .BuildAsync();
```

#### 2. Transaction Chain Building
```csharp
var chainBuilder = new TransactionChainBuilder(provider);

var chain = await chainBuilder
    .AddTransaction(async context =>
    {
        // First transaction - normal payment
        return await TransactionBuilder.Create(protocolParams)
            .WithMempoolContext(context)
            .AddInputFromAddress(senderAddress)
            .AddOutput(receiverAddress, new Lovelace(10_000_000))
            .BuildAsync();
    })
    .AddTransaction(async context =>
    {
        // Second transaction - spend change from first transaction
        return await TransactionBuilder.Create(protocolParams)
            .WithMempoolContext(context)  // Has access to pending UTxOs
            .AddInputFromAddress(senderAddress)  // Can spend change from first tx
            .AddOutput(anotherAddress, new Lovelace(5_000_000))
            .BuildAsync();
    })
    .BuildChainAsync();

// Submit transactions in sequence
foreach (var tx in chain.Transactions)
{
    await provider.SubmitTransactionAsync(tx);
    await Task.Delay(1000); // Brief delay between submissions
}
```

#### 3. Real-time Mempool Monitoring
```csharp
using var monitor = new MempoolMonitor(provider);

monitor.MempoolChanged += (sender, args) =>
{
    Console.WriteLine($"Mempool changed: {args.NewTransactions.Count} new, {args.ConfirmedTransactions.Count} confirmed");
    
    // Rebuild any pending transactions if UTxOs changed
    if (args.AffectedUtxos.Any())
    {
        _ = Task.Run(() => RefreshPendingTransactions(args.AffectedUtxos));
    }
};

monitor.StartMonitoring(TimeSpan.FromSeconds(15));
```

### Benefits
- **Eliminates Double-Spending**: Automatic detection of consumed UTxOs
- **Enables Transaction Chaining**: Build transactions that depend on pending outputs
- **Improved Reliability**: Reduces "UTXO not found" errors by 90%+
- **Better Developer Experience**: No manual UTXO tracking required
- **Real-time Awareness**: Monitor mempool changes for dynamic applications

### Implementation Considerations
- **Caching Strategy**: Cache mempool state to reduce API calls
- **Fallback Mechanism**: Graceful degradation when mempool data unavailable
- **Performance Impact**: Minimize additional API calls through batching
- **State Consistency**: Handle race conditions between mempool updates

---

## Priority 1: Enhanced Fee Calculation Framework

**Priority**: P1 (Critical)  
**Impact**: Accurate fee estimation, configurable parameters, improved precision

### Problem Statement

Current fee calculation has several issues:
- Hardcoded magic numbers (`SizeIncrement = 25600`, `Multiplier = 1.2`)
- Floating-point arithmetic for currency calculations introduces rounding errors
- No validation of input parameters
- Missing configuration for different network parameters

### Technical Specification

#### 1. Configurable Fee Calculator
```csharp
public class ReferenceScriptFeeCalculator
{
    private readonly FeeCalculationConfig _config;
    
    public record FeeCalculationConfig(
        uint SizeIncrement = 25600,
        decimal TierMultiplier = 1.2m,  // Use decimal for currency precision
        uint MaxTiers = 10,
        bool ValidateInputs = true
    );
    
    public ulong CalculateReferenceScriptFee(
        ReadOnlySpan<byte> scriptBytes, 
        ulong baseCostPerByte)
    {
        if (_config.ValidateInputs)
        {
            ArgumentException.ThrowIfNullOrEmpty(nameof(scriptBytes));
            if (baseCostPerByte == 0)
                throw new ArgumentException("Base cost per byte must be greater than zero");
        }
        
        decimal accumulatedFee = 0;
        decimal currentTierPrice = baseCostPerByte;
        int remainingSize = scriptBytes.Length;
        uint currentTier = 0;

        while (remainingSize > 0 && currentTier < _config.MaxTiers)
        {
            int sizeThisTier = Math.Min(remainingSize, (int)_config.SizeIncrement);
            accumulatedFee += sizeThisTier * currentTierPrice;
            
            remainingSize -= sizeThisTier;
            currentTierPrice *= _config.TierMultiplier;
            currentTier++;
        }

        if (accumulatedFee > ulong.MaxValue)
            throw new OverflowException("Calculated fee exceeds maximum value");
            
        return (ulong)accumulatedFee;
    }
}
```

### Benefits
- **Accurate Calculations**: Decimal precision eliminates rounding errors
- **Configurable Parameters**: Network-specific fee parameters
- **Better Error Handling**: Validation and overflow protection
- **Transparent Breakdown**: Clear fee component visibility

---

## Priority 2: Advanced Coin Selection Strategies

**Priority**: P2 (High)  
**Impact**: Better UTXO efficiency, reduced transaction costs, multiple algorithm options

### Problem Statement

Current coin selection implementation has limitations:
- Single algorithm (Largest First) may not always be optimal
- O(n²) complexity in asset matching degrades performance
- No optimization for transaction size or UTXO consolidation
- Limited configurability for different use cases

### Technical Specification

#### 1. Strategy Pattern for Coin Selection
```csharp
public interface ICoinSelectionStrategy
{
    CoinSelectionResult SelectCoins(
        IReadOnlyList<ResolvedInput> availableUtxos,
        IReadOnlyList<Value> requiredAmounts,
        CoinSelectionConstraints constraints);
    
    string Name { get; }
    CoinSelectionMetrics GetMetrics();
}

public record CoinSelectionConstraints(
    int MaxInputs = int.MaxValue,
    ulong MaxFeeBuffer = 5_000_000,
    bool PreferSingleAsset = true,
    bool OptimizeForSize = false,
    TimeSpan MaxSelectionTime = default);
```

### Benefits
- **Multiple Strategies**: Choose optimal algorithm for use case
- **Better Performance**: Reduced complexity and cached lookups
- **Size Optimization**: Minimize transaction size when needed
- **Configurable Behavior**: Flexible constraints and preferences

---

## Priority 2: Transaction Builder Validation Framework

**Priority**: P2 (High)  
**Impact**: Early error detection, better developer experience, transaction correctness

### Problem Statement

Current transaction building lacks validation:
- Errors only detected at build time or later
- Generic exception messages provide little guidance
- No incremental validation during construction
- Difficult to debug complex transaction building issues

### Technical Specification

#### 1. Validation Framework
```csharp
public class TransactionBuilder
{
    private readonly List<ValidationError> _validationErrors = new();
    private readonly ValidationConfig _config;
    
    public TransactionBuilder AddInput(TransactionInput input)
    {
        var validator = new InputValidator(_config);
        var result = validator.Validate(input, body);
        
        if (!result.IsValid)
            _validationErrors.AddRange(result.Errors);
        else
            body = body with { Inputs = new CborDefListWithTag<TransactionInput>([.. body.Inputs.GetValue(), input]) };
            
        return this;
    }
}
```

### Benefits
- **Early Detection**: Catch errors during construction
- **Better Messages**: Specific, actionable error descriptions
- **Incremental Validation**: Check validity at any point
- **Type Safety**: Rich error types for programmatic handling

---

## Priority 3: Simplified Template Builder API

**Priority**: P3 (Medium)  
**Impact**: Improved developer experience, reduced complexity, better maintainability

### Problem Statement

The current `TransactionTemplateBuilder` is complex and difficult to use:
- Complex delegate signatures with unclear parameters
- No compile-time validation of template configurations
- Error messages are runtime-only and often unclear
- 1,137 lines of code in a single class makes it hard to maintain

### Benefits
- **Simplified API**: Clear, strongly-typed specifications
- **Compile-time Safety**: Source generation ensures correctness
- **Better Error Messages**: Specific template validation errors
- **Maintainable Code**: Smaller, focused components

---

## Success Metrics

### Performance Targets
- **Mempool Awareness**: 90% reduction in UTXO-related transaction failures
- **Fee Calculation**: 50% improvement in calculation speed through caching and decimal precision
- **Coin Selection**: Support for 10,000+ UTxOs without performance degradation
- **Memory Usage**: 30% reduction in allocations during transaction building

### Quality Targets
- **Test Coverage**: >90% line coverage for all transaction building components
- **Error Handling**: 100% of public APIs have structured error handling
- **Mempool Accuracy**: <1% false positive rate in UTXO availability detection
- **Transaction Success Rate**: >99% successful transaction submission with mempool awareness

### Developer Experience Targets
- **Build Time**: <5 seconds from requirements to signed transaction
- **Error Clarity**: All errors include actionable guidance
- **Chain Building**: Simple API for dependent transaction sequences
- **Debugging**: Rich diagnostic information for all transaction failures

---

## Implementation Strategy

### Phase 1: Mempool Foundation
- Implement `IMempoolAwareUtxoProvider` interface
- Create mempool context and state management
- Add mempool-aware coin selection

### Phase 2: Enhanced Building
- Transaction chain builder for dependent transactions
- Real-time mempool monitoring
- Enhanced fee calculation with decimal precision

### Phase 3: Validation & UX
- Comprehensive validation framework
- Simplified template builder API
- Rich error handling and diagnostics

### Phase 4: Advanced Features
- Performance optimizations and caching
- Advanced coin selection strategies
- Source generation for transaction templates

The mempool-aware transaction building represents a significant advancement that will eliminate many common developer pain points while enabling more sophisticated transaction patterns like chaining and batching.