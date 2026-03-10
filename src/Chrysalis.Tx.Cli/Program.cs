using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Utils;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core;
using Chrysalis.Codec.Types.Cardano.Core.Common;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Tx.Builders;
using Chrysalis.Tx.Extensions;
using Chrysalis.Tx.Models;
using Chrysalis.Tx.Models.Cbor;
using Chrysalis.Tx.Providers;
using Chrysalis.Tx.Cli;
using Chrysalis.Tx.Utils;
using Chrysalis.Wallet.Models.Enums;
using WalletAddress = Chrysalis.Wallet.Models.Addresses.Address;
using Chrysalis.Wallet.Models.Keys;
using Chrysalis.Wallet.Words;

// ── Configuration ──────────────────────────────────────────────────────────
const string ValidatorScriptHash = "9d60ad506e49b83ce19f46e1c436ea0b933b0f4b9813fda33a48fcb1";
const string DeployUtxoTxHash = "3fb269ab071b9cefb81d6133516d36309a087c244a1401f88693b452163fd237";
const ulong DeployUtxoIndex = 0;
const string DeployAddress = "addr_test1wzwkpt2sdeyms08pnarwr3pkag9exwc0fwvp8ldr8fy0evgs34phf";

// USDM: policyId (56 hex chars = 28 bytes) + assetName hex "USDM"
const string UsdmUnit = "e31a9fbefc4375176e289ca986067fa179440409abfe58f27fb8d0b95553444d";
const ulong OrderUsdmAmount = 5_000_000; // 5 USDM (6 decimal places)
const ulong MinAdaInScript = 2_000_000;  // 2 ADA min-ADA in script UTxO
const ulong FillPercent = 90;            // Fill 90% of the order
const long PriceNum = 1;
const long PriceDen = 1;                 // 1 lovelace per USDM unit = 1 ADA per USDM

string? blockfrostKey = Environment.GetEnvironmentVariable("BLOCKFROST_API_KEY");
if (string.IsNullOrWhiteSpace(blockfrostKey))
{
    await Console.Error.WriteLineAsync("Error: BLOCKFROST_API_KEY environment variable is required.").ConfigureAwait(false);
    await Console.Error.WriteLineAsync("  export BLOCKFROST_API_KEY=preview...").ConfigureAwait(false);
    return 1;
}

byte[] usdmPolicyId = Convert.FromHexString(UsdmUnit[..56]);
byte[] usdmAssetName = Convert.FromHexString(UsdmUnit[56..]);

Console.WriteLine("=== Chrysalis E2E: Create Order → Fill Order (USDM/ADA) ===");
Console.WriteLine($"  Order: Sell {OrderUsdmAmount} USDM units, price {PriceNum}/{PriceDen} (lovelace per USDM unit)");
Console.WriteLine($"  Fill:  Buy {FillPercent}% of locked USDM, pay equal ADA");
Console.WriteLine();

// ── Step 1: Restore wallet ──────────────────────────────────────────────────
Console.WriteLine("1. Restoring Wizard demo wallet...");
const string WizardMnemonic = "time gold spatial rookie simple rely across divorce man ugly train great into loud myself ancient omit addict beauty truck found space planet garage";
Mnemonic mnemonic = Mnemonic.Restore(WizardMnemonic, English.Words);
PrivateKey accountKey = mnemonic
    .GetRootKey("")
    .Derive(PurposeType.Shelley, DerivationType.HARD)
    .Derive(CoinType.Ada, DerivationType.HARD)
    .Derive(0, DerivationType.HARD);

PrivateKey paymentKey = accountKey.Derive(RoleType.ExternalChain).Derive(0);
PublicKey paymentPubKey = paymentKey.GetPublicKey();
PrivateKey stakingKey = accountKey.Derive(RoleType.Staking).Derive(0);
PublicKey stakingPubKey = stakingKey.GetPublicKey();

WalletAddress walletAddress = WalletAddress.FromPublicKeys(
    NetworkType.Testnet, AddressType.Base, paymentPubKey, stakingPubKey);
string walletBech32 = walletAddress.ToBech32();

byte[] paymentKeyHash = walletAddress.GetPaymentKeyHash()
    ?? throw new InvalidOperationException("Could not derive payment key hash.");

Console.WriteLine($"   Wallet:   {walletBech32}");
Console.WriteLine($"   Key hash: {Convert.ToHexStringLower(paymentKeyHash)}");

// ── Step 2: Derive script address ───────────────────────────────────────────
Console.WriteLine("2. Deriving script address...");
byte[] scriptHash = Convert.FromHexString(ValidatorScriptHash);
WalletAddress scriptAddr = new(NetworkType.Testnet, AddressType.EnterpriseScriptPayment, scriptHash, null);
string scriptAddress = scriptAddr.ToBech32();
Console.WriteLine($"   Script: {scriptAddress}");

// ── Step 3: Connect ─────────────────────────────────────────────────────────
Console.WriteLine("3. Connecting to Blockfrost (Preview)...");
using Blockfrost provider = new(blockfrostKey, NetworkType.Preview);

using HttpClient bfClient = new();
bfClient.BaseAddress = new Uri("https://cardano-preview.blockfrost.io/api/v0/");
bfClient.DefaultRequestHeaders.Add("project_id", blockfrostKey);

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// PHASE 1: CREATE ORDER
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
Console.WriteLine();
Console.WriteLine("══ PHASE 1: CREATE ORDER ══");

// Datum: FixedPrice, USDM/ADA
//   asset_pair.1st = received = ADA   (enters the UTxO from the filler)
//   asset_pair.2nd = offered  = USDM  (leaves the UTxO to the filler)
TemplateWizardDatum orderDatum = new(
    "wizard"u8.ToArray(),
    new WizardDatum(
        new FixedPriceKind(),
        new AssetPair(
            new WizardAsset([], []),
            new WizardAsset(usdmPolicyId, usdmAssetName)),
        new OneWay(new RationalC(PriceNum, PriceDen)),
        new None<Swap>(),
        new Signature(paymentKeyHash)));

Console.WriteLine("4. Building create order transaction...");

CreateOrderParams createParams = new(
    OwnerAddress: walletBech32,
    LovelaceAmount: MinAdaInScript,
    PriceNum: PriceNum,
    PriceDen: PriceDen,
    ContractAddress: scriptAddress,
    ChangeAddress: walletBech32);

TransactionTemplate<CreateOrderParams> createTemplate =
    TransactionTemplateBuilder.Create<CreateOrderParams>(provider)
        .AddStaticParty("change", walletBech32, true)
        .AddOutput((options, _, _) =>
        {
            options.To = "contract";
            options.Amount = CreateMultiAssetValue(MinAdaInScript, usdmPolicyId, usdmAssetName, OrderUsdmAmount);
            options.SetDatum(orderDatum);
        })
        .AddMetadata(_ => CreateMetadata("Chrysalis E2E: create order"))
        .Build();

Stopwatch sw = Stopwatch.StartNew();
ITransaction createUnsigned = await createTemplate(createParams).ConfigureAwait(false);
sw.Stop();
Console.WriteLine($"   Built ({sw.ElapsedMilliseconds}ms)");

ITransaction createSigned = createUnsigned.Sign(paymentKey);
byte[] createCbor = CborSerializer.Serialize(createSigned);
Console.WriteLine($"   Signed TX: {createCbor.Length} bytes");

Console.WriteLine("5. Submitting create order...");
sw.Restart();
string createTxId = await provider.SubmitTransactionAsync(createSigned).ConfigureAwait(false);
sw.Stop();
Console.WriteLine($"   TxHash: {createTxId} ({sw.ElapsedMilliseconds}ms)");
Console.WriteLine($"   https://preview.cardanoscan.io/transaction/{createTxId}");

Console.WriteLine("6. Waiting for confirmation...");
bool createConfirmed = await WaitForConfirmations(bfClient, createTxId).ConfigureAwait(false);
if (!createConfirmed)
{
    await Console.Error.WriteLineAsync("   Create order timed out!").ConfigureAwait(false);
    return 1;
}
Console.WriteLine("   Confirmed!");

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// PHASE 2: FILL ORDER
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
Console.WriteLine();
Console.WriteLine("══ PHASE 2: FILL ORDER ══");

Console.WriteLine("7. Waiting for script UTxO indexing...");
ResolvedInput? ourUtxo = await WaitForScriptUtxo(provider, scriptAddress, createTxId).ConfigureAwait(false);

if (ourUtxo is null)
{
    await Console.Error.WriteLineAsync($"   Could not find our UTxO {createTxId} at script address!").ConfigureAwait(false);
    return 1;
}

Console.WriteLine($"   Found: {createTxId}#{ourUtxo.Outref.Index}");

// Parse datum and original assets from the UTxO
PostAlonzoTransactionOutput ourOutput = (PostAlonzoTransactionOutput)ourUtxo.Output;
InlineDatumOption ourInlineDatum = (InlineDatumOption)ourOutput.Datum!;
byte[] datumCbor = ourInlineDatum.Data.Value.ToArray();

Dictionary<string, ulong> originalAssets = ExtractAssets(ourOutput.Amount);
ulong originalUsdm = originalAssets.GetValueOrDefault(UsdmUnit, 0UL);
if (originalUsdm == 0)
{
    await Console.Error.WriteLineAsync("   No USDM found in script UTxO!").ConfigureAwait(false);
    return 1;
}

ulong amountToBuy = originalUsdm * FillPercent / 100;
if (amountToBuy == 0)
{
    await Console.Error.WriteLineAsync("   Fill amount rounded to 0.").ConfigureAwait(false);
    return 1;
}

// Verify datum round-trip
TemplateWizardDatum parsedDatum = CborSerializer.Deserialize<TemplateWizardDatum>(datumCbor);
byte[] reserializedDatum = CborSerializer.Serialize(parsedDatum);
Console.WriteLine($"   Datum round-trip: {(reserializedDatum.AsSpan().SequenceEqual(datumCbor) ? "OK" : "MISMATCH")}");
Console.WriteLine($"   Original USDM: {originalUsdm}, buying {amountToBuy} ({FillPercent}%)");

// Get current slot for validity window
ulong currentSlot = await GetCurrentSlot(bfClient).ConfigureAwait(false);
Console.WriteLine($"   Current slot: {currentSlot}");

// Calculate the continuing output value after the fill:
//   subtract amountToBuy USDM, add ceil(amountToBuy * price) lovelace
IValue continuingValue = CalculateNewValue(originalAssets, UsdmUnit, amountToBuy, PriceNum, PriceDen);

Console.WriteLine("8. Building fill order transaction...");

FillOrderParams fillParams = new(
    ScriptUtxoTxHash: createTxId,
    ScriptUtxoIndex: ourUtxo.Outref.Index,
    ScriptAddress: scriptAddress,
    DatumCbor: datumCbor,
    AmountToBuy: amountToBuy,
    PriceNum: PriceNum,
    PriceDen: PriceDen,
    DeployUtxoTxHash: DeployUtxoTxHash,
    DeployUtxoIndex: DeployUtxoIndex,
    DeployAddress: DeployAddress,
    ChangeAddress: walletBech32);

TransactionTemplate<FillOrderParams> fillTemplate =
    TransactionTemplateBuilder.Create<FillOrderParams>(provider)
        .AddStaticParty("change", walletBech32, true)
        .AddReferenceInput((options, param) =>
        {
            options.From = "deployAddress";
            options.UtxoRef = CborFactory.CreateTransactionInput(
                Convert.FromHexString(param.DeployUtxoTxHash),
                param.DeployUtxoIndex);
            options.Id = "deployRef";
        })
        .AddInput((options, param) =>
        {
            options.From = "scriptAddress";
            options.UtxoRef = CborFactory.CreateTransactionInput(
                Convert.FromHexString(param.ScriptUtxoTxHash),
                param.ScriptUtxoIndex);
            options.Id = "scriptInput";
            options.RedeemerBuilder = (mapping, _, _) =>
            {
                (_, Dictionary<string, ulong> outputIndices) = mapping.GetInput("scriptInput");
                ulong outputIndex = outputIndices.TryGetValue("continuingOutput", out ulong idx) ? idx : 0;
                // offer_second = true: filler buys the second/offered asset (USDM)
                BuyRedeemer data = new((long)outputIndex, new PlutusTrue(), new None<OracleFeeds>());
                return new Redeemer<ICborType>(RedeemerTag.Spend, 0, data, CborFactory.CreateExUnits(1_000_000, 400_000_000));
            };
        })
        .AddOutput((options, param, _) =>
        {
            options.To = "scriptAddress";
            options.Amount = continuingValue;
            options.SetDatum(CborSerializer.Deserialize<TemplateWizardDatum>(param.DatumCbor));
            options.AssociatedInputId = "scriptInput";
            options.Id = "continuingOutput";
        })
        .SetValidTo(currentSlot + 300)
        .AddMetadata(_ => CreateMetadata("Chrysalis E2E: fill order"))
        .Build();

sw.Restart();
ITransaction fillUnsigned = await fillTemplate(fillParams).ConfigureAwait(false);
sw.Stop();
Console.WriteLine($"   Built ({sw.ElapsedMilliseconds}ms)");

ITransaction fillSigned = fillUnsigned.Sign(paymentKey);
byte[] fillCbor = CborSerializer.Serialize(fillSigned);
Console.WriteLine($"   Signed TX: {fillCbor.Length} bytes");

Console.WriteLine("9. Submitting fill order...");
sw.Restart();
string fillTxId = await provider.SubmitTransactionAsync(fillSigned).ConfigureAwait(false);
sw.Stop();
Console.WriteLine($"   TxHash: {fillTxId} ({sw.ElapsedMilliseconds}ms)");
Console.WriteLine($"   https://preview.cardanoscan.io/transaction/{fillTxId}");

Console.WriteLine("10. Waiting for confirmation...");
bool fillConfirmed = await WaitForConfirmations(bfClient, fillTxId).ConfigureAwait(false);
if (!fillConfirmed)
{
    await Console.Error.WriteLineAsync("   Fill order timed out!").ConfigureAwait(false);
    return 1;
}
Console.WriteLine("   Confirmed!");

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// PHASE 3: CLOSE ORDER (reclaim remaining position)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
Console.WriteLine();
Console.WriteLine("══ PHASE 3: CLOSE ORDER ══");

Console.WriteLine("11. Waiting for fill's continuing UTxO indexing...");
ResolvedInput? fillUtxo = await WaitForScriptUtxo(provider, scriptAddress, fillTxId).ConfigureAwait(false);

if (fillUtxo is null)
{
    await Console.Error.WriteLineAsync($"   Could not find fill UTxO {fillTxId} at script address!").ConfigureAwait(false);
    return 1;
}

Console.WriteLine($"   Found: {fillTxId}#{fillUtxo.Outref.Index}");

Console.WriteLine("12. Building close order transaction...");

CloseOrderParams closeParams = new(
    ScriptUtxoTxHash: fillTxId,
    ScriptUtxoIndex: fillUtxo.Outref.Index,
    ScriptAddress: scriptAddress,
    DeployUtxoTxHash: DeployUtxoTxHash,
    DeployUtxoIndex: DeployUtxoIndex,
    DeployAddress: DeployAddress,
    OwnerAddress: walletBech32,
    ChangeAddress: walletBech32);

TransactionTemplate<CloseOrderParams> closeTemplate =
    TransactionTemplateBuilder.Create<CloseOrderParams>(provider)
        .AddStaticParty("change", walletBech32, true)
        .AddRequiredSigner("owner")
        .AddReferenceInput((options, param) =>
        {
            options.From = "deployAddress";
            options.UtxoRef = CborFactory.CreateTransactionInput(
                Convert.FromHexString(param.DeployUtxoTxHash),
                param.DeployUtxoIndex);
            options.Id = "deployRef";
        })
        .AddInput((options, param) =>
        {
            options.From = "scriptAddress";
            options.UtxoRef = CborFactory.CreateTransactionInput(
                Convert.FromHexString(param.ScriptUtxoTxHash),
                param.ScriptUtxoIndex);
            options.Id = "scriptInput";
            options.RedeemerBuilder = (_, _, _) =>
                new Redeemer<ICborType>(RedeemerTag.Spend, 0, new CloseRedeemer(), CborFactory.CreateExUnits(500_000, 200_000_000));
        })
        .AddMetadata(_ => CreateMetadata("Chrysalis E2E: close order"))
        .Build();

sw.Restart();
ITransaction closeUnsigned = await closeTemplate(closeParams).ConfigureAwait(false);
sw.Stop();
Console.WriteLine($"   Built ({sw.ElapsedMilliseconds}ms)");

ITransaction closeSigned = closeUnsigned.Sign(paymentKey);
byte[] closeCbor = CborSerializer.Serialize(closeSigned);
Console.WriteLine($"   Signed TX: {closeCbor.Length} bytes");

Console.WriteLine("13. Submitting close order...");
sw.Restart();
string closeTxId = await provider.SubmitTransactionAsync(closeSigned).ConfigureAwait(false);
sw.Stop();
Console.WriteLine($"   TxHash: {closeTxId} ({sw.ElapsedMilliseconds}ms)");
Console.WriteLine($"   https://preview.cardanoscan.io/transaction/{closeTxId}");

Console.WriteLine("14. Waiting for confirmation...");
bool closeConfirmed = await WaitForConfirmations(bfClient, closeTxId).ConfigureAwait(false);
if (!closeConfirmed)
{
    await Console.Error.WriteLineAsync("   Close order timed out!").ConfigureAwait(false);
    return 1;
}
Console.WriteLine("   Confirmed!");

Console.WriteLine();
Console.WriteLine("══════════════════════════════════════════════════");
Console.WriteLine("=== SUCCESS: Create → Fill → Close E2E confirmed! ===");
Console.WriteLine($"  Create TX: {createTxId}");
Console.WriteLine($"  Fill TX:   {fillTxId}");
Console.WriteLine($"  Close TX:  {closeTxId}");
Console.WriteLine("══════════════════════════════════════════════════");

return 0;

// ── Helpers ────────────────────────────────────────────────────────────────

// Build a LovelaceWithMultiAsset value for the create-order UTxO
static IValue CreateMultiAssetValue(ulong lovelace, byte[] policyId, byte[] assetName, ulong amount)
{
    Dictionary<ReadOnlyMemory<byte>, ulong> tokenBundle = new(ReadOnlyMemoryComparer.Instance)
    {
        [(ReadOnlyMemory<byte>)assetName] = amount
    };
    Dictionary<ReadOnlyMemory<byte>, TokenBundleOutput> multiAsset = new(ReadOnlyMemoryComparer.Instance)
    {
        [(ReadOnlyMemory<byte>)policyId] = CborFactory.CreateTokenBundleOutput(tokenBundle)
    };
    return CborFactory.CreateLovelaceWithMultiAsset(lovelace, CborFactory.CreateMultiAssetOutput(multiAsset));
}

// Extract all assets from a UTxO Value into a string-keyed dict.
// Keys: "lovelace" for ADA, or "{policyId hex}{assetName hex}" for tokens.
static Dictionary<string, ulong> ExtractAssets(IValue value)
{
    Dictionary<string, ulong> assets = [];
    switch (value)
    {
        case Lovelace l:
            assets["lovelace"] = l.Amount;
            break;
        case LovelaceWithMultiAsset m:
            assets["lovelace"] = m.Amount;
            foreach ((ReadOnlyMemory<byte> policyId, TokenBundleOutput bundle) in m.MultiAsset.Value)
            {
                foreach ((ReadOnlyMemory<byte> assetName, ulong qty) in bundle.Value)
                {
                    string unit = Convert.ToHexStringLower(policyId.Span) + Convert.ToHexStringLower(assetName.Span);
                    assets[unit] = qty;
                }
            }
            break;
        default:
            break;
    }
    return assets;
}

// Port of FillLogic.CalculateNewValue from Wizard demo.
// Subtracts assetToBuy from the UTxO, adds ceil(amountToBuy * price) lovelace.
static IValue CalculateNewValue(
    Dictionary<string, ulong> originalAssets,
    string assetToBuy,
    ulong amountToBuy,
    long priceNum,
    long priceDen)
{
    BigInteger sellAmount = ((new BigInteger(amountToBuy) * priceNum) + priceDen - 1) / priceDen;
    Dictionary<string, ulong> newAssets = new(originalAssets);

    ulong currentBuy = newAssets.GetValueOrDefault(assetToBuy, 0UL);
    if (amountToBuy >= currentBuy)
    {
        _ = newAssets.Remove(assetToBuy);
    }
    else
    {
        newAssets[assetToBuy] = currentBuy - amountToBuy;
    }

    newAssets["lovelace"] = newAssets.GetValueOrDefault("lovelace", 0UL) + (ulong)sellAmount;

    return CreateValueFromAssets(newAssets);
}

// Build a Value from a string-keyed asset dictionary.
static IValue CreateValueFromAssets(Dictionary<string, ulong> assets)
{
    ulong lovelace = assets.GetValueOrDefault("lovelace", 0UL);
    Dictionary<string, Dictionary<string, ulong>> byPolicy = [];

    foreach ((string unit, ulong qty) in assets)
    {
        if (unit == "lovelace" || qty == 0)
        {
            continue;
        }

        string policyHex = unit[..56];
        string nameHex = unit.Length > 56 ? unit[56..] : "";
        if (!byPolicy.TryGetValue(policyHex, out Dictionary<string, ulong>? tokens))
        {
            tokens = [];
            byPolicy[policyHex] = tokens;
        }
        tokens[nameHex] = qty;
    }

    if (byPolicy.Count == 0)
    {
        return CborFactory.CreateLovelace(lovelace);
    }

    Dictionary<ReadOnlyMemory<byte>, TokenBundleOutput> multiAsset = new(ReadOnlyMemoryComparer.Instance);
    foreach ((string policyHex, Dictionary<string, ulong> tokens) in byPolicy)
    {
        Dictionary<ReadOnlyMemory<byte>, ulong> bundle = new(ReadOnlyMemoryComparer.Instance);
        foreach ((string nameHex, ulong qty) in tokens)
        {
            bundle[Convert.FromHexString(nameHex)] = qty;
        }
        multiAsset[Convert.FromHexString(policyHex)] = CborFactory.CreateTokenBundleOutput(bundle);
    }

    return CborFactory.CreateLovelaceWithMultiAsset(lovelace, CborFactory.CreateMultiAssetOutput(multiAsset));
}

static Metadata CreateMetadata(string message)
{
    ITransactionMetadatum msgMap = CborFactory.CreateMetadatumMap(new Dictionary<ITransactionMetadatum, ITransactionMetadatum>
    {
        {
            CborFactory.CreateMetadataText("msg"),
            CborFactory.CreateMetadatumList([CborFactory.CreateMetadataText(message)])
        }
    });

    return CborFactory.CreateMetadata(new Dictionary<ulong, ITransactionMetadatum>
    {
        { 674, msgMap }
    });
}

static async Task<bool> WaitForConfirmations(
    HttpClient client,
    string txHash,
    int requiredConfirmations = 1,
    int maxWaitSeconds = 240,
    int pollSeconds = 5)
{
    Stopwatch sw = Stopwatch.StartNew();
    for (int elapsed = 0; elapsed < maxWaitSeconds; elapsed += pollSeconds)
    {
        await Task.Delay(TimeSpan.FromSeconds(pollSeconds)).ConfigureAwait(false);

        using HttpResponseMessage response = await client
            .GetAsync(new Uri($"txs/{txHash}", UriKind.Relative))
            .ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            ulong confirmations = 0;
            if (TryReadUlong(root, "confirmations", out ulong confValue))
            {
                confirmations = confValue;
            }
            else if (TryReadUlong(root, "block_height", out ulong blockHeight) && blockHeight > 0)
            {
                if (requiredConfirmations <= 1)
                {
                    confirmations = 1;
                }
                else
                {
                    ulong latestHeight = await GetCurrentBlockHeight(client).ConfigureAwait(false);
                    confirmations = latestHeight >= blockHeight
                        ? latestHeight - blockHeight + 1
                        : 0;
                }
            }
            else if (root.TryGetProperty("block", out JsonElement block) &&
                block.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(block.GetString()))
            {
                confirmations = 1;
            }

            if (confirmations >= (ulong)requiredConfirmations)
            {
                sw.Stop();
                Console.Write($" ({sw.ElapsedMilliseconds / 1000}s, {confirmations} conf)");
                return true;
            }

            Console.Write($".[{confirmations}]");
            continue;
        }

        Console.Write(".");
    }

    Console.WriteLine();
    return false;
}

static bool TryReadUlong(JsonElement root, string property, out ulong value)
{
    value = 0;
    if (!root.TryGetProperty(property, out JsonElement elem))
    {
        return false;
    }

    if (elem.ValueKind == JsonValueKind.Number && elem.TryGetUInt64(out ulong numberValue))
    {
        value = numberValue;
        return true;
    }

    if (elem.ValueKind == JsonValueKind.String &&
        ulong.TryParse(elem.GetString(), out ulong stringValue))
    {
        value = stringValue;
        return true;
    }

    return false;
}

static async Task<ResolvedInput?> WaitForScriptUtxo(
    ICardanoDataProvider provider,
    string scriptAddress,
    string txHash,
    int maxWaitSeconds = 120,
    int pollSeconds = 4)
{
    for (int elapsed = 0; elapsed < maxWaitSeconds; elapsed += pollSeconds)
    {
        List<ResolvedInput> scriptUtxos = await provider.GetUtxosAsync([scriptAddress]).ConfigureAwait(false);
        foreach (ResolvedInput utxo in scriptUtxos)
        {
            string currentTxHash = Convert.ToHexStringLower(utxo.Outref.TransactionId.Span);
            if (currentTxHash == txHash)
            {
                return utxo;
            }
        }

        await Task.Delay(TimeSpan.FromSeconds(pollSeconds)).ConfigureAwait(false);
        Console.Write(".");
    }

    Console.WriteLine();
    return null;
}

static async Task<ulong> GetCurrentSlot(HttpClient client)
{
    using HttpResponseMessage response = await client
        .GetAsync(new Uri("blocks/latest", UriKind.Relative))
        .ConfigureAwait(false);

    _ = response.EnsureSuccessStatusCode();
    string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    using JsonDocument doc = JsonDocument.Parse(json);
    return doc.RootElement.GetProperty("slot").GetUInt64();
}

static async Task<ulong> GetCurrentBlockHeight(HttpClient client)
{
    using HttpResponseMessage response = await client
        .GetAsync(new Uri("blocks/latest", UriKind.Relative))
        .ConfigureAwait(false);

    _ = response.EnsureSuccessStatusCode();
    string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    using JsonDocument doc = JsonDocument.Parse(json);
    return doc.RootElement.GetProperty("height").GetUInt64();
}
