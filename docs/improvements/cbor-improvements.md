# CBOR Module Improvement Roadmap

## Current State Assessment

### Strengths
- ✅ **High Performance**: 3.7x faster than previous implementation
- ✅ **Source Generation**: Compile-time optimized serialization 
- ✅ **Era Support**: Full support for Shelley → Conway eras
- ✅ **Type Safety**: Strong typing with comprehensive validation
- ✅ **Memory Efficiency**: Raw data preservation and smart caching

### Current Limitations
- ❌ **Byron Era**: No support for Byron-era blocks and transactions
- ❌ **Memory Allocation**: Still using `byte[]` and `ReadOnlyMemory<byte>` causing allocations
- ❌ **Pipeline Integration**: Limited streaming/pipeline processing capabilities
- ❌ **Large Object Handling**: Performance degrades with very large CBOR structures

---

## Priority 1: Byron Era Support

### Problem Statement
Chrysalis currently only supports post-Byron eras (Shelley onwards), limiting its use for:
- Full blockchain synchronization from genesis
- Historical data analysis applications
- Archive/indexing services requiring complete chain coverage

### Implementation Approach

#### Byron Transaction Format
```csharp
[CborSerializable]
[CborConstr(0)]
public partial record ByronTransaction(
    [CborOrder(0)] List<ByronTransactionInput> Inputs,
    [CborOrder(1)] List<ByronTransactionOutput> Outputs,
    [CborOrder(2)] ByronTransactionAttributes Attributes
) : Transaction;

[CborSerializable] 
[CborConstr(0)]
public partial record ByronTransactionInput(
    [CborOrder(0)] byte TransactionInputType,  // 0 for regular input
    [CborOrder(1)] ByronTxIn TransactionInput
) : CborBase;
```

#### Byron Address Format
- Different address encoding (Base58 instead of Bech32)
- Legacy derivation paths and key formats
- Bootstrap witness signatures

### Breaking Changes
- **None** - Byron types will be additive to existing era union types
- New `ByronTransaction : Transaction` maintains polymorphic compatibility


---

## Priority 1: Zero-Allocation CBOR Processing

### Problem Statement
Current implementation uses memory allocations that impact performance in high-throughput scenarios:
- `byte[]` allocations for CBOR data
- `ReadOnlyMemory<byte>` creates managed memory pressure
- Garbage collection pauses during intensive processing

### Technical Solution: Pipeline-Based Processing

#### New Pipeline API Design
```csharp
public interface ICborPipelineReader<T> where T : CborBase
{
    ValueTask<T?> ReadAsync(PipeReader pipeReader, CancellationToken cancellationToken = default);
    ValueTask<List<T>> ReadBatchAsync(PipeReader pipeReader, int maxCount, CancellationToken cancellationToken = default);
}

public interface ICborPipelineWriter<T> where T : CborBase  
{
    ValueTask WriteAsync(PipeWriter pipeWriter, T value, CancellationToken cancellationToken = default);
    ValueTask WriteBatchAsync(PipeWriter pipeWriter, IEnumerable<T> values, CancellationToken cancellationToken = default);
}
```

#### Streaming Implementation
```csharp
[CborSerializable]
public partial record Block : CborBase, ICborStreamable
{
    // Generate pipeline-optimized Read/Write methods
    public static async ValueTask<Block?> ReadFromPipelineAsync(PipeReader reader)
    {
        // Use span-based reading directly from pipe buffer
        // No intermediate byte[] allocation
    }
}
```

### Memory Benefits
- **Zero allocations** for small-to-medium CBOR structures
- **Reduced GC pressure** by ~60-80% in streaming scenarios
- **Better throughput** for chain sync and bulk processing

### Breaking Changes
- **Minor**: New overloads added, existing APIs remain unchanged
- Migration path through extension methods


---

## Priority 1: Automatic Raw Data Invalidation

**Priority**: P1 (Critical)  
**Impact**: Prevents data corruption from stale cached CBOR data

### Problem Statement

Types implementing `ICborPreserveRaw` cache the original CBOR bytes in the `Raw` property for round-trip consistency. However, when the object is modified (e.g., signing a transaction), the cached raw data becomes stale and invalid, potentially causing:

- Hash inconsistencies in blockchain contexts
- Silent data corruption during re-serialization  
- Difficult-to-debug issues when round-tripping modified objects
- Manual `Raw = null` assignments scattered throughout the codebase

### Current Manual Approach
```csharp
// Transaction signing - manual invalidation required
return tx with
{
    TransactionWitnessSet = updatedWitnessSet,
    Raw = null  // Manual invalidation - easy to forget!
};
```

### Technical Specification

#### 1. Automatic Invalidation via Source Generation
```csharp
[CborSerializable]
public partial record Transaction(...) : CborBase, ICborPreserveRaw
{
    // Generated method to invalidate raw data on any property change
    private void InvalidateRaw()
    {
        if (this is ICborPreserveRaw)
        {
            Raw = null;
        }
    }
    
    // Enhanced with expression to auto-invalidate
    public Transaction with { ... } => /* generated with InvalidateRaw() call */
}
```

#### 2. Source Generator Enhancement
```csharp
// In CborSerializerCodeGen.cs - enhance generation for ICborPreserveRaw types
if (metadata.ShouldPreserveRaw)
{
    // Generate with-expression override that auto-invalidates
    sb.AppendLine($@"
    public new {className} with {{ get; init; }} => this with 
    {{
        Raw = null  // Auto-invalidate on any property change
    }};");
}
```

#### 3. Extension Method Pattern
```csharp
public static class CborPreserveRawExtensions
{
    public static T WithInvalidatedRaw<T>(this T self) where T : CborBase, ICborPreserveRaw
    {
        if (self.Raw != null)
        {
            return self with { Raw = null };
        }
        return self;
    }
    
    public static T EnsureRawInvalidated<T>(this T self, bool wasModified) where T : CborBase, ICborPreserveRaw
    {
        if (wasModified && self.Raw != null)
        {
            return self with { Raw = null };
        }
        return self;
    }
}
```

#### 4. Smart Caching Strategy
```csharp
public abstract partial record CborBase
{
    private ReadOnlyMemory<byte>? _raw;
    private int _hashCode = 0; // Cached hash for change detection
    
    public ReadOnlyMemory<byte>? Raw 
    { 
        get => _raw;
        set 
        {
            _raw = value;
            _hashCode = value?.GetHashCode() ?? 0;
        }
    }
    
    // Automatically invalidate if object structure changed
    protected virtual bool ShouldInvalidateRaw()
    {
        if (_raw == null) return false;
        
        // Quick hash check - if object changed, invalidate
        var currentHash = GetHashCode();
        if (currentHash != _hashCode)
        {
            _raw = null;
            return true;
        }
        return false;
    }
}
```

### Implementation Strategy

#### Phase 1: Source Generation Enhancement
- Enhance code generator to auto-invalidate raw data on record modifications
- Generate smart `with` expressions for `ICborPreserveRaw` types
- Add compile-time validation for manual `Raw = null` assignments

#### Phase 2: Extension Methods & Utilities
- Create extension methods for explicit raw invalidation
- Add debugging utilities to detect stale raw data
- Implement smart caching with change detection

#### Phase 3: Cleanup & Testing
- Remove manual `Raw = null` assignments throughout codebase  
- Add comprehensive tests for raw data invalidation scenarios
- Performance testing to ensure minimal overhead

### Benefits
- **Eliminates Data Corruption**: Automatic invalidation prevents stale cached data
- **Reduces Manual Error**: No need for manual `Raw = null` assignments
- **Maintains Performance**: Smart caching minimizes serialization overhead
- **Improves Debugging**: Clear invalidation patterns make issues easier to track

### Breaking Changes
- **None**: Enhancement is additive to existing functionality
- Existing manual `Raw = null` assignments remain valid but become redundant

---

## Priority 2: Advanced Performance Optimizations

### Memory Pool Integration
```csharp
public static class CborSerializer
{
    public static T Deserialize<T>(ReadOnlySequence<byte> sequence, MemoryPool<byte>? pool = null) where T : CborBase;
    
    // Rent from pool instead of allocating
    public static PooledMemoryOwner<byte> SerializePooled<T>(T value, MemoryPool<byte>? pool = null) where T : CborBase;
}
```

### SIMD Optimizations
- Vectorized CBOR parsing for large arrays
- Batch processing of primitive types
- Platform-specific optimizations (.NET 8+ Vector512 support)


---

## Priority 2: Enhanced Streaming Support

### Incremental Parsing
Support for parsing large CBOR structures without loading entire structure into memory:

```csharp
public interface ICborIncrementalParser<T>
{
    IAsyncEnumerable<T> ParseAsync(Stream stream);
    IAsyncEnumerable<T> ParseAsync(PipeReader reader);
}

// Usage for large block processing
await foreach(var transaction in blockParser.ParseTransactionsAsync(blockStream))
{
    // Process transaction without loading entire block
}
```


---

## Priority 3: Developer Experience Improvements

### Enhanced Source Generator Diagnostics
- Better error messages for invalid CBOR attributes
- IntelliSense support for generated properties  
- Compile-time validation of CBOR structure compatibility

### Debugging Support
```csharp
[CborSerializable(GenerateDebugView = true)]
public partial record ComplexType : CborBase
{
    // Auto-generates DebuggerDisplay and ToString with CBOR hex
}
```


---


---

## Success Metrics

### Performance Targets
- **Byron Support**: Process Byron blocks at same speed as current Shelley blocks
- **Memory**: Reduce allocation rate by 60-80% for streaming scenarios  
- **Throughput**: Maintain current 4,500+ blocks/s processing rate

### Quality Targets
- **100% Byron Era Coverage**: Support all Byron block types from Cardano mainnet
- **Zero Breaking Changes**: All improvements maintain API compatibility
- **Comprehensive Testing**: >95% test coverage for new Byron functionality

---

## Risk Assessment

### High Risk
- **Byron CBOR Complexity**: Historical Byron formats may have undocumented edge cases
- **Performance Regression**: Zero-allocation changes could impact existing performance

### Mitigation Strategies
- Extensive testing with real Byron blockchain data
- Feature flags for new implementations 
- Comprehensive benchmarking at each milestone
- Gradual rollout with fallback to current implementation