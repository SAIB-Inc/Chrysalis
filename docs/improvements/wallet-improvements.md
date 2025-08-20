# Wallet Module Improvement Roadmap

## Current State Assessment

### Strengths
- ✅ **Solid Cryptographic Foundation**: Correct BIP32/BIP39 implementation with Cardano-specific Ed25519 handling
- ✅ **CIP-8 Message Signing**: Complete COSE implementation with excellent compatibility testing
- ✅ **Address Generation**: Support for Base, Enterprise, Script, and Delegation address types
- ✅ **Type Safety**: Strong typing with modern C# record types and enums
- ✅ **Bech32 Implementation**: Correct encoding/decoding with proper checksum validation
- ✅ **Security Practices**: Proper entropy generation and PBKDF2 key derivation

### Current Limitations
- ❌ **No Hot/Cold Wallet Separation**: Missing fundamental security architecture patterns
- ❌ **Limited CIP Support**: Missing key wallet-related Cardano Improvement Proposals
- ❌ **No Multi-Signature Support**: Cannot handle threshold signatures or native script wallets
- ❌ **Missing Cold Storage**: No air-gapped, hardware, or paper wallet implementations
- ❌ **No Watch-Only Wallets**: Cannot track addresses without private keys
- ❌ **Incomplete Address Support**: Missing Byron addresses and incomplete Pointer address implementation

---

## Priority 1: Hot and Cold Wallet Architecture

**Priority**: P1 (Critical)  
**Impact**: Foundation for secure wallet operations, enterprise adoption, proper security model

### Problem Statement

Current wallet implementation lacks the fundamental Hot/Cold wallet security paradigm that's essential for:
- **Enterprise security**: Separating signing keys from online systems
- **Risk management**: Different security models for different use cases
- **Multi-signature operations**: Coordinating between hot and cold signers
- **Regulatory compliance**: Air-gapped storage for institutional requirements

### Proposed Hot/Cold Wallet Architecture

#### 1. Core Wallet Security Model
```csharp
// Base wallet interface - all wallets implement this
public interface ICardanoWallet
{
    WalletInfo Info { get; }
    NetworkType Network { get; }
    WalletSecurity SecurityModel { get; }
    
    Task<Address> GetReceiveAddressAsync(int index = 0);
    Task<Address> GetChangeAddressAsync(int index = 0);
    Task<IEnumerable<Address>> GetAllAddressesAsync();
    Task<Balance> GetBalanceAsync();
}

// Hot wallet - online, can sign transactions
public interface IHotWallet : ICardanoWallet
{
    Task<byte[]> SignTransactionAsync(ReadOnlyMemory<byte> txBody);
    Task<string> SignMessageAsync(string message, Address signingAddress);
    Task<Transaction> BuildAndSignTransactionAsync(TransactionBuilder builder);
}

// Cold wallet - offline, secure signing
public interface IColdWallet : ICardanoWallet  
{
    Task<byte[]> SignTransactionAsync(ReadOnlyMemory<byte> txBody, ColdWalletContext context);
    Task<string> SignMessageAsync(string message, Address signingAddress, ColdWalletContext context);
    Task<PartialSignature> CreatePartialSignatureAsync(ReadOnlyMemory<byte> txBody, DerivationPath signingPath);
}

// Watch-only wallet - can observe but not sign
public interface IWatchOnlyWallet : ICardanoWallet
{
    Task SyncWithBlockchainAsync();
    Task<IEnumerable<Transaction>> GetTransactionHistoryAsync();
    Task<IEnumerable<Utxo>> GetUtxosAsync();
}

public enum WalletSecurity
{
    Hot,           // Online, immediate signing
    Cold,          // Offline, manual verification required
    WatchOnly,     // Read-only, no signing capability
    MultiSigHot,   // Hot wallet participating in multi-sig
    MultiSigCold   // Cold wallet participating in multi-sig
}
```

#### 2. Hot Wallet Implementations
```csharp
// Software hot wallet - keys in memory
public class SoftwareHotWallet : IHotWallet
{
    private readonly Mnemonic _mnemonic;
    private readonly SecureKeyCache _keyCache;
    
    public async Task<byte[]> SignTransactionAsync(ReadOnlyMemory<byte> txBody)
    {
        using var signingKey = await GetSigningKeyAsync();
        return signingKey.Sign(HashUtil.Blake2b256(txBody.Span));
    }
    
    public async Task<Transaction> BuildAndSignTransactionAsync(TransactionBuilder builder)
    {
        var tx = await builder.BuildAsync();
        var signature = await SignTransactionAsync(CborSerializer.Serialize(tx.TransactionBody));
        return tx.AddSignature(signature);
    }
}

// Web3 hot wallet - browser-based
public class Web3HotWallet : IHotWallet
{
    private readonly ICip30WalletApi _walletApi;
    
    public async Task<byte[]> SignTransactionAsync(ReadOnlyMemory<byte> txBody)
    {
        var txHex = Convert.ToHexString(txBody.Span);
        var signedHex = await _walletApi.SignTxAsync(txHex);
        return Convert.FromHexString(signedHex);
    }
}

// Mobile hot wallet - with biometric authentication
public class MobileHotWallet : IHotWallet
{
    private readonly IBiometricAuthenticator _biometrics;
    private readonly ISecureStorage _secureStorage;
    
    public async Task<byte[]> SignTransactionAsync(ReadOnlyMemory<byte> txBody)
    {
        // Require biometric authentication for signing
        if (!await _biometrics.AuthenticateAsync("Sign transaction"))
            throw new UnauthorizedAccessException("Biometric authentication failed");
        
        using var key = await _secureStorage.RetrieveKeyAsync("signing_key");
        return key.Sign(HashUtil.Blake2b256(txBody.Span));
    }
}
```

#### 3. Cold Wallet Implementations
```csharp
// Hardware cold wallet (Ledger, Trezor, etc.)
public class HardwareColdWallet : IColdWallet
{
    private readonly IHardwareDevice _device;
    
    public async Task<byte[]> SignTransactionAsync(ReadOnlyMemory<byte> txBody, ColdWalletContext context)
    {
        // Display transaction details on device for verification
        await _device.DisplayTransactionAsync(txBody, context.VerificationDetails);
        
        // Wait for user confirmation on device
        if (!await _device.WaitForUserConfirmationAsync())
            throw new UserCancelledException("User rejected transaction on device");
        
        return await _device.SignAsync(txBody);
    }
}

// Air-gapped cold wallet - QR code communication
public class AirGappedColdWallet : IColdWallet
{
    public async Task<byte[]> SignTransactionAsync(ReadOnlyMemory<byte> txBody, ColdWalletContext context)
    {
        // Generate QR code for transaction
        var qrData = CreateSigningQrCode(txBody, context);
        
        // Display QR code for offline device to scan
        await context.DisplayQrCodeAsync(qrData);
        
        // Wait for signed transaction QR code response
        var signedQrData = await context.ScanSignedQrCodeAsync();
        
        return ExtractSignatureFromQrCode(signedQrData);
    }
}

// Paper wallet cold storage
public class PaperColdWallet : IColdWallet
{
    private readonly string _privateKeyWif;
    
    public async Task<byte[]> SignTransactionAsync(ReadOnlyMemory<byte> txBody, ColdWalletContext context)
    {
        // Manual verification required for paper wallets
        var txDetails = await ParseTransactionDetailsAsync(txBody);
        
        if (!await context.VerifyTransactionManuallyAsync(txDetails))
            throw new UserCancelledException("Manual transaction verification failed");
        
        var privateKey = PrivateKey.FromWif(_privateKeyWif);
        return privateKey.Sign(HashUtil.Blake2b256(txBody.Span));
    }
}

// Multi-signature cold wallet
public class MultiSigColdWallet : IColdWallet
{
    private readonly List<IColdWallet> _coldSigners;
    private readonly NativeScript _multiSigScript;
    private readonly int _threshold;
    
    public async Task<PartialSignature> CreatePartialSignatureAsync(ReadOnlyMemory<byte> txBody, DerivationPath signingPath)
    {
        var context = new ColdWalletContext
        {
            MultiSigInfo = new MultiSigInfo(_threshold, _coldSigners.Count),
            SigningPath = signingPath
        };
        
        var signature = await _coldSigners.First().SignTransactionAsync(txBody, context);
        
        return new PartialSignature
        {
            Signature = signature,
            SigningPath = signingPath,
            PublicKey = await DerivePublicKeyAsync(signingPath)
        };
    }
}
```

#### 4. Watch-Only Wallet Implementation
```csharp
public class WatchOnlyWallet : IWatchOnlyWallet
{
    private readonly ICardanoDataProvider _provider;
    private readonly List<Address> _watchedAddresses;
    
    public async Task SyncWithBlockchainAsync()
    {
        foreach (var address in _watchedAddresses)
        {
            var utxos = await _provider.GetUtxosAsync([address.ToBech32()]);
            var transactions = await _provider.GetTransactionsAsync(address.ToBech32());
            
            await UpdateLocalStateAsync(address, utxos, transactions);
        }
    }
    
    public async Task<Balance> GetBalanceAsync()
    {
        var totalLovelace = 0UL;
        var assets = new Dictionary<string, ulong>();
        
        foreach (var address in _watchedAddresses)
        {
            var utxos = await GetUtxosAsync(address);
            foreach (var utxo in utxos)
            {
                totalLovelace += utxo.Output.Amount().Lovelace();
                
                foreach (var asset in utxo.Output.Amount().MultiAsset()?.Values() ?? [])
                {
                    // Aggregate multi-asset balances
                    var assetId = CreateAssetId(asset);
                    assets[assetId] = assets.GetValueOrDefault(assetId) + asset.Value;
                }
            }
        }
        
        return new Balance(new Lovelace(totalLovelace), assets);
    }
}
```

#### 5. Cold Wallet Context and Communication
```csharp
public class ColdWalletContext
{
    public TransactionVerificationDetails? VerificationDetails { get; set; }
    public MultiSigInfo? MultiSigInfo { get; set; }
    public DerivationPath? SigningPath { get; set; }
    public string? UserMessage { get; set; }
    
    // QR Code communication for air-gapped wallets
    public Func<string, Task>? DisplayQrCodeAsync { get; set; }
    public Func<Task<string>>? ScanSignedQrCodeAsync { get; set; }
    
    // Manual verification for paper wallets
    public Func<TransactionDetails, Task<bool>>? VerifyTransactionManuallyAsync { get; set; }
}

public record TransactionVerificationDetails(
    string From,
    string To, 
    ulong Amount,
    ulong Fee,
    string? Message = null);

public record MultiSigInfo(int Threshold, int TotalSigners);

public record PartialSignature(
    byte[] Signature,
    DerivationPath SigningPath,
    PublicKey PublicKey,
    DateTimeOffset CreatedAt = default);
```

### Benefits
- **Clear Security Model**: Explicit separation between online and offline operations
- **Flexible Architecture**: Support for various cold storage methods (hardware, air-gapped, paper)
- **Enterprise Ready**: Proper risk management through hot/cold separation
- **Multi-Signature Support**: Both hot and cold signers in threshold schemes
- **Watch-Only Capability**: Portfolio tracking without exposure to private keys

---

## Priority 1: Multi-Signature Wallet Support in Cardano Context

**Priority**: P1 (Critical)  
**Impact**: Enterprise adoption, shared custody, advanced smart contract integration

### Problem Statement

Multi-signature wallets are essential for:
- **Shared custody**: Multiple parties controlling funds
- **Corporate treasuries**: Requiring multiple approvals for transactions
- **Smart contract integration**: Complex authorization schemes
- **Risk management**: Distributing signing authority

### Cardano Multi-Signature Implementation

#### 1. Native Script Multi-Signature
```csharp
public class NativeScriptMultiSigWallet : ICardanoWallet
{
    private readonly NativeScript _script;
    private readonly List<ICardanoWallet> _signers; // Mix of hot and cold wallets
    private readonly int _threshold;
    
    public async Task<Transaction> CreateMultiSigTransactionAsync(
        TransactionBuilder builder,
        int requiredSignatures)
    {
        var unsignedTx = await builder.BuildAsync();
        var signatures = new List<VKeyWitness>();
        var signingCount = 0;
        
        foreach (var signer in _signers)
        {
            if (signingCount >= requiredSignatures) break;
            
            byte[] signature;
            
            // Handle different signer types
            switch (signer)
            {
                case IHotWallet hotWallet:
                    signature = await hotWallet.SignTransactionAsync(
                        CborSerializer.Serialize(unsignedTx.TransactionBody));
                    break;
                    
                case IColdWallet coldWallet:
                    var context = new ColdWalletContext
                    {
                        MultiSigInfo = new MultiSigInfo(_threshold, _signers.Count),
                        VerificationDetails = ExtractVerificationDetails(unsignedTx)
                    };
                    signature = await coldWallet.SignTransactionAsync(
                        CborSerializer.Serialize(unsignedTx.TransactionBody), context);
                    break;
                    
                default:
                    continue; // Skip watch-only wallets
            }
            
            var publicKey = await GetSignerPublicKeyAsync(signer);
            signatures.Add(new VKeyWitness(publicKey.Key, signature));
            signingCount++;
        }
        
        if (signingCount < requiredSignatures)
            throw new InsufficientSignaturesException($"Only {signingCount} of {requiredSignatures} required signatures obtained");
        
        return unsignedTx with 
        { 
            TransactionWitnessSet = unsignedTx.TransactionWitnessSet.AddSignatures(signatures)
        };
    }
}
```

#### 2. Time-Lock Multi-Signature
```csharp
public class TimeLockMultiSigWallet : NativeScriptMultiSigWallet
{
    private readonly uint _validAfterSlot;
    private readonly uint? _validBeforeSlot;
    
    public async Task<bool> IsValidForSigningAsync()
    {
        var currentSlot = await GetCurrentSlotAsync();
        
        if (currentSlot < _validAfterSlot)
            return false; // Transaction not valid yet
            
        if (_validBeforeSlot.HasValue && currentSlot > _validBeforeSlot)
            return false; // Transaction expired
            
        return true;
    }
    
    protected override async Task<Transaction> CreateTransactionWithTimeLockAsync(TransactionBuilder builder)
    {
        if (!await IsValidForSigningAsync())
            throw new TimeLockViolationException("Transaction is outside valid time window");
        
        // Set validity interval in transaction
        builder = builder
            .SetValidAfter(_validAfterSlot)
            .SetValidBefore(_validBeforeSlot);
            
        return await base.CreateMultiSigTransactionAsync(builder, _threshold);
    }
}
```

#### 3. Hierarchical Multi-Signature (Corporate Treasury)
```csharp
public class HierarchicalMultiSigWallet : ICardanoWallet
{
    private readonly Dictionary<AuthorizationLevel, MultiSigConfig> _levels;
    
    public enum AuthorizationLevel
    {
        Daily,      // Small amounts, fewer signatures
        Weekly,     // Medium amounts, more signatures  
        Monthly,    // Large amounts, executive approval
        Emergency   // Special emergency procedures
    }
    
    public record MultiSigConfig(
        int Threshold,
        List<ICardanoWallet> Signers,
        ulong MaxAmount,
        TimeSpan Cooldown);
    
    public async Task<Transaction> CreateHierarchicalTransactionAsync(
        TransactionBuilder builder,
        ulong amount)
    {
        var level = DetermineAuthorizationLevel(amount);
        var config = _levels[level];
        
        if (await IsInCooldownPeriodAsync(level))
            throw new CooldownViolationException($"Transaction level {level} is in cooldown period");
        
        var multiSigWallet = new NativeScriptMultiSigWallet(
            config.Signers, 
            config.Threshold);
            
        var transaction = await multiSigWallet.CreateMultiSigTransactionAsync(
            builder, 
            config.Threshold);
        
        await RecordTransactionForCooldownAsync(level, transaction);
        return transaction;
    }
    
    private AuthorizationLevel DetermineAuthorizationLevel(ulong amount)
    {
        return amount switch
        {
            <= 100_000_000 => AuthorizationLevel.Daily,    // <= 100 ADA
            <= 1_000_000_000 => AuthorizationLevel.Weekly, // <= 1,000 ADA
            <= 10_000_000_000 => AuthorizationLevel.Monthly, // <= 10,000 ADA
            _ => AuthorizationLevel.Emergency
        };
    }
}
```

#### 4. Plutus Script Multi-Signature
```csharp
public class PlutusMultiSigWallet : ICardanoWallet
{
    private readonly byte[] _plutusScript;
    private readonly List<ICardanoWallet> _signers;
    private readonly PlutusData _redeemerTemplate;
    
    public async Task<Transaction> CreatePlutusMultiSigTransactionAsync(
        TransactionBuilder builder,
        PlutusData customRedeemer)
    {
        var partialSignatures = new List<PartialSignature>();
        
        // Collect partial signatures from all signers
        foreach (var signer in _signers)
        {
            if (signer is IColdWallet coldWallet)
            {
                var context = new ColdWalletContext
                {
                    UserMessage = "Plutus multi-signature transaction",
                    VerificationDetails = ExtractVerificationDetails(builder)
                };
                
                var partialSig = await coldWallet.CreatePartialSignatureAsync(
                    BuildTransactionBytes(builder), 
                    DerivationPath.Default);
                    
                partialSignatures.Add(partialSig);
            }
        }
        
        // Combine signatures in redeemer
        var redeemer = CreateMultiSigRedeemer(partialSignatures, customRedeemer);
        
        return await builder
            .AddPlutusScript(_plutusScript)
            .AddRedeemer(redeemer)
            .BuildAsync();
    }
}
```

### Benefits
- **Enterprise Security**: Proper multi-party control over funds
- **Flexible Thresholds**: Different signature requirements for different amounts
- **Time-Lock Support**: Transaction validity windows for additional security
- **Mixed Signer Types**: Combination of hot and cold wallet signers
- **Cardano Native**: Full integration with native scripts and Plutus contracts

---

## Priority 2: Comprehensive CIP Support

**Priority**: P2 (High)  
**Impact**: Ecosystem compatibility, standards compliance, future-proof architecture

### Key CIP Implementations for Hot/Cold Wallet Context

#### 1. CIP-1852: HD Wallets (Enhanced for Hot/Cold)
```csharp
public static class Cip1852
{
    // Hot wallet derivation - frequent use paths
    public static DerivationPath HotWalletReceive(uint account, uint index) =>
        new($"m/1852'/1815'/{account}'/0/{index}");
    
    // Cold wallet derivation - high-security paths  
    public static DerivationPath ColdWalletSigning(uint account) =>
        new($"m/1852'/1815'/{account}'/2/0");
        
    // Multi-sig participant paths
    public static DerivationPath MultiSigParticipant(uint account, uint participantIndex) =>
        new($"m/1852'/1815'/{account}'/3/{participantIndex}");
}

public class Cip1852HotWallet : SoftwareHotWallet
{
    protected override DerivationPath GetReceivePath(int index) =>
        Cip1852.HotWalletReceive(_accountIndex, (uint)index);
}

public class Cip1852ColdWallet : HardwareColdWallet  
{
    protected override DerivationPath GetSigningPath() =>
        Cip1852.ColdWalletSigning(_accountIndex);
}
```

#### 2. CIP-30: dApp Bridge (Hot Wallet Context)
```csharp
public class Cip30HotWalletBridge : ICip30WalletApi
{
    private readonly IHotWallet _hotWallet;
    
    public async Task<string> SignTxAsync(string tx, bool partialSign = false)
    {
        // Hot wallets can sign immediately
        var txBytes = Convert.FromHexString(tx);
        var signature = await _hotWallet.SignTransactionAsync(txBytes);
        return Convert.ToHexString(signature);
    }
}

// Cold wallets cannot directly bridge to dApps for security reasons
public class Cip30ColdWalletProxy : ICip30WalletApi
{
    private readonly IColdWallet _coldWallet;
    
    public async Task<string> SignTxAsync(string tx, bool partialSign = false)
    {
        // Cold wallet requires explicit context and verification
        throw new SecurityViolationException(
            "Cold wallets cannot directly sign dApp transactions. Use hot wallet or manual verification process.");
    }
}
```

#### 3. CIP-95: Multi-Signature dApp Bridge
```csharp
public class Cip95MultiSigBridge : ICip95ConwayApi
{
    private readonly NativeScriptMultiSigWallet _multiSigWallet;
    
    public async Task<string> SignTxAsync(string tx, bool partialSign = false)
    {
        if (partialSign)
        {
            // Return partial signature for multi-sig coordination
            return await CreatePartialSignatureAsync(tx);
        }
        else
        {
            // Attempt to collect required signatures
            var requiredSigs = _multiSigWallet.GetThreshold();
            var signedTx = await _multiSigWallet.CreateMultiSigTransactionAsync(
                TransactionBuilder.FromHex(tx), requiredSigs);
            return Convert.ToHexString(CborSerializer.Serialize(signedTx));
        }
    }
}
```

---

## Success Metrics

### Security Architecture
- **Hot/Cold Separation**: Clear security boundaries between online and offline operations
- **Multi-Signature Support**: Threshold signatures with mixed hot/cold signers
- **Risk Management**: Appropriate security levels for different transaction amounts
- **Audit Trail**: Complete logging of all wallet operations and authorizations

### Wallet Type Coverage
- **Hot Wallets**: Software, Web3, Mobile with biometric authentication
- **Cold Wallets**: Hardware (Ledger/Trezor), Air-gapped, Paper wallets
- **Watch-Only**: Portfolio tracking and transaction monitoring
- **Multi-Signature**: Native script and Plutus script implementations

### Enterprise Features
- **Corporate Treasury**: Hierarchical authorization with time locks
- **Compliance**: Audit trails and regulatory reporting
- **High Availability**: Redundant signing infrastructure
- **Integration**: APIs for enterprise wallet management systems

### Developer Experience
- **Unified API**: Consistent interface across all wallet types
- **Security Guidance**: Clear documentation on when to use hot vs cold wallets
- **Examples**: Complete implementations for common enterprise patterns
- **Performance**: Sub-second operations for hot wallets, secure verification for cold wallets

---

## Implementation Strategy

### Phase 1: Hot/Cold Foundation
- Implement core IHotWallet, IColdWallet, and IWatchOnlyWallet interfaces
- Create basic software hot wallet and watch-only implementations
- Establish security patterns and cold wallet context system

### Phase 2: Multi-Signature Support
- Implement native script multi-signature wallets
- Add support for mixed hot/cold signer combinations
- Create hierarchical authorization for corporate use cases

### Phase 3: Cold Wallet Implementations
- Hardware wallet integration (Ledger, Trezor)
- Air-gapped wallet with QR code communication
- Paper wallet support with manual verification

### Phase 4: Advanced Features and CIP Support
- Complete CIP implementations (30, 95, etc.)
- Plutus script multi-signature support
- Enterprise management and monitoring tools

The hot/cold wallet architecture provides a secure, flexible foundation that matches real-world security practices while enabling sophisticated multi-signature and enterprise use cases in the Cardano ecosystem.