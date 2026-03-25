using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Utils;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core;
using Chrysalis.Codec.Types.Cardano.Core.Common;
using IPlutusData = Chrysalis.Codec.Types.Cardano.Core.Common.IPlutusData;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Tx.Builders;
using Chrysalis.Tx.Extensions;
using Chrysalis.Tx.Models;
using Chrysalis.Tx.Models.Cbor;
using Chrysalis.Tx.Providers;
using Chrysalis.Tx.Cli;
using Chrysalis.Codec.Types.Cardano.Core.Protocol;
using Chrysalis.Codec.Types.Cardano.Core.Scripts;
using Chrysalis.Wallet.Models.Enums;
using WalletAddress = Chrysalis.Wallet.Models.Addresses.Address;
using Chrysalis.Wallet.Models.Keys;
using Chrysalis.Codec.Extensions;
using Chrysalis.Network.Cbor.LocalStateQuery;
using Chrysalis.Wallet.Words;

// ── Configuration ──────────────────────────────────────────────────────────
// Load the parameterized validator from wizard.plutus envelope (oracle key already applied)
string envelopePath = Path.Combine(AppContext.BaseDirectory, "wizard.plutus");
using JsonDocument envelope = JsonDocument.Parse(File.ReadAllText(envelopePath));
byte[] scriptCborHex = Convert.FromHexString(envelope.RootElement.GetProperty("cborHex").GetString()!);
byte[] innerScriptBytes = CborSerializer.Deserialize<ReadOnlyMemory<byte>>(scriptCborHex).ToArray();
IScript validatorScriptInstance = PlutusV3Script.Create(3, innerScriptBytes);
string ValidatorScriptHash = validatorScriptInstance.HashHex();
// Deploy UTxO — discovered dynamically if null, or override via env vars
string? DeployUtxoTxHash = Environment.GetEnvironmentVariable("DEPLOY_TX_HASH");
ulong DeployUtxoIndex = ulong.TryParse(Environment.GetEnvironmentVariable("DEPLOY_TX_INDEX"), out ulong dIdx) ? dIdx : 0;

// TESTV2: policyId (56 hex chars = 28 bytes) + assetName hex "TESTV2"
const string UsdmUnit = "e31a9fbefc4375176e289ca986067fa179440409abfe58f27fb8d0b9544553545632";
const ulong OrderUsdmAmount = 5_000_000; // 5 TESTV2 units
const ulong MinAdaInScript = 2_000_000;  // 2 ADA min-ADA in script UTxO
const ulong FillPercent = 90;            // Fill 90% of the order
const long PriceNum = 1;
const long PriceDen = 1;                 // 1 lovelace per USDM unit = 1 ADA per USDM

// ── Mode selection ────────────────────────────────────────────────────────
string mode = "HIGH";
foreach (string arg in args)
{
    if (arg.StartsWith("--mode", StringComparison.OrdinalIgnoreCase))
    {
        string modeValue = arg.Contains('=', StringComparison.Ordinal) ? arg.Split('=')[1] : args[Array.IndexOf(args, arg) + 1];
        mode = modeValue.ToUpperInvariant();
    }
}

// ── Blockfrost key ────────────────────────────────────────────────────────
string? blockfrostKey = Environment.GetEnvironmentVariable("BLOCKFROST_API_KEY");
if (string.IsNullOrWhiteSpace(blockfrostKey))
{
    await Console.Error.WriteLineAsync("Error: BLOCKFROST_API_KEY environment variable is required.").ConfigureAwait(false);
    await Console.Error.WriteLineAsync("  export BLOCKFROST_API_KEY=preview...  OR  export BLOCKFROST_API_KEY=preprod...").ConfigureAwait(false);
    return 1;
}

// ── Detect network from API key prefix ───────────────────────────────────
NetworkType networkType = blockfrostKey.StartsWith("preprod", StringComparison.OrdinalIgnoreCase)
    ? NetworkType.Preprod
    : NetworkType.Preview;
string networkLabel = networkType == NetworkType.Preprod ? "Preprod" : "Preview";
string blockfrostBaseUrl = networkType == NetworkType.Preprod
    ? "https://cardano-preprod.blockfrost.io/api/v0/"
    : "https://cardano-preview.blockfrost.io/api/v0/";
string cardanoscanBase = networkType == NetworkType.Preprod
    ? "https://preprod.cardanoscan.io"
    : "https://preview.cardanoscan.io";

// ── Banner ─────────────────────────────────────────────────────────────────
string modeLabel = mode switch
{
    "MID" => "TxBuilder (mid-level)",
    "LOW" => "TransactionBuilder (low-level)",
    _ => "TransactionTemplateBuilder (high-level)"
};

Console.WriteLine($"=== Chrysalis E2E: Create Order → Fill Order → Close Order ===");
Console.WriteLine($"  Mode:  {modeLabel}");
Console.WriteLine($"  Order: Sell {OrderUsdmAmount} USDM units, price {PriceNum}/{PriceDen} (lovelace per USDM unit)");
Console.WriteLine($"  Fill:  Buy {FillPercent}% of locked USDM, pay equal ADA");

// ── Step 1: Restore wallet ─────────────────────────────────────────────────
Console.WriteLine();
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
string paymentKeyHashHex = Convert.ToHexStringLower(paymentKeyHash);

Console.WriteLine($"   Wallet:   {walletBech32}");
Console.WriteLine($"   Key hash: {paymentKeyHashHex}");

// ── Step 2: Derive script address ───────────────────────────────────────────
Console.WriteLine("2. Deriving script address...");
byte[] scriptHash = Convert.FromHexString(ValidatorScriptHash);
WalletAddress scriptAddr = new(NetworkType.Testnet, AddressType.EnterpriseScriptPayment, scriptHash, null);
string scriptAddress = scriptAddr.ToBech32();
Console.WriteLine($"   Script: {scriptAddress}");

// ── Step 3: Connect ─────────────────────────────────────────────────────────
Console.WriteLine($"3. Connecting to Blockfrost ({networkLabel})...");
using Blockfrost provider = new(blockfrostKey, networkType);

using HttpClient bfClient = new();
bfClient.BaseAddress = new Uri(blockfrostBaseUrl);
bfClient.DefaultRequestHeaders.Add("project_id", blockfrostKey);

// ── Shared derived values ───────────────────────────────────────────────────
byte[] usdmPolicyId = Convert.FromHexString(UsdmUnit[..56]);
byte[] usdmAssetName = Convert.FromHexString(UsdmUnit[56..]);

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// PHASE 0: MINT TEST TOKENS (if wallet doesn't have them)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
Console.WriteLine();
Console.WriteLine("══ PHASE 0: MINT TEST TOKENS ══");

// Check if wallet already has the test token
List<ResolvedInput> preCheckUtxos = await provider.GetUtxosAsync([walletBech32]).ConfigureAwait(false);
string usdmUnitUpper = UsdmUnit.ToUpperInvariant();
ulong totalTestTokens = 0;
foreach (ResolvedInput u in preCheckUtxos)
{
    IValue amount = u.Output switch
    {
        PostAlonzoTransactionOutput p => p.Amount,
        AlonzoTransactionOutput a => a.Amount,
        _ => Lovelace.Create(0)
    };
    if (amount is LovelaceWithMultiAsset lma && lma.MultiAsset.Value is not null)
    {
        foreach ((ReadOnlyMemory<byte> pid, TokenBundleOutput bundle) in lma.MultiAsset.Value)
        {
            foreach ((ReadOnlyMemory<byte> name, ulong qty) in bundle.Value)
            {
                string unit = Convert.ToHexString(pid.Span) + Convert.ToHexString(name.Span);
                if (unit.Equals(usdmUnitUpper, StringComparison.OrdinalIgnoreCase))
                {
                    totalTestTokens += qty;
                }
            }
        }
    }
}
bool hasTestToken = totalTestTokens >= OrderUsdmAmount;

if (hasTestToken)
{
    Console.WriteLine("   Tokens already present, skipping mint.");
}
else
{
    Console.WriteLine("   Minting test tokens...");

    // Build native script: ScriptPubKey(0, paymentKeyHash)
    ScriptPubKey sigScript = ScriptPubKey.Create(0, paymentKeyHash);
    string policyHex = UsdmUnit[..56];
    string assetNameHex = UsdmUnit[56..];

    TxBuilder mintBuilder = new TxBuilder(provider)
        .SetChangeAddress(walletBech32)
        .AddUnspentOutputs(preCheckUtxos)
        .AddMint(policyHex, new Dictionary<string, long> { [assetNameHex] = 10_000_000 }, sigScript);

    PostMaryTransaction mintUnsigned = await mintBuilder.Complete().ConfigureAwait(false);
    string mintTxId = await SubmitAndConfirm(provider, bfClient, mintUnsigned, paymentKey,
        "mint test tokens", 4, cardanoscanBase).ConfigureAwait(false);
    Console.WriteLine($"   Minted 10M TESTV2 tokens in tx {mintTxId}");

    // Wait for minted UTxOs to appear
    preCheckUtxos = await WaitForWalletUtxos(provider, walletBech32, mintTxId).ConfigureAwait(false);
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// PHASE 0.5: DEPLOY VALIDATOR REFERENCE SCRIPT (if not already deployed)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
Console.WriteLine();
Console.WriteLine("══ DEPLOY VALIDATOR ══");

string deployAddress = scriptAddress; // deploy to the script address itself

if (DeployUtxoTxHash is not null)
{
    Console.WriteLine($"   Using existing deploy UTxO: {DeployUtxoTxHash}#{DeployUtxoIndex}");
}
else
{
    // Check if the validator is already deployed at the script address
    List<ResolvedInput> scriptUtxos = await provider.GetUtxosAsync([deployAddress]).ConfigureAwait(false);
    ResolvedInput? existingDeploy = scriptUtxos.FirstOrDefault(u =>
        u.Output is PostAlonzoTransactionOutput p && p.ScriptRef is not null);

    if (existingDeploy is not null)
    {
        DeployUtxoTxHash = Convert.ToHexStringLower(existingDeploy.Outref.TransactionId.Span);
        DeployUtxoIndex = existingDeploy.Outref.Index;
        Console.WriteLine($"   Found existing deploy: {DeployUtxoTxHash}#{DeployUtxoIndex}");
    }
    else
    {
        Console.WriteLine("   Deploying validator reference script...");
        Console.WriteLine($"   Script hash: {ValidatorScriptHash} (from blueprint codegen)");

        List<ResolvedInput> deployUtxos = await provider.GetUtxosAsync([walletBech32]).ConfigureAwait(false);
        PostMaryTransaction deployTx = await new TxBuilder(provider)
            .SetChangeAddress(walletBech32)
            .AddUnspentOutputs(deployUtxos)
            .DeployScript(validatorScriptInstance, deployAddress)
            .Complete().ConfigureAwait(false);

        string deployTxId = await SubmitAndConfirm(provider, bfClient, deployTx, paymentKey,
            "deploy validator", 4, cardanoscanBase).ConfigureAwait(false);

        DeployUtxoTxHash = deployTxId;
        DeployUtxoIndex = 0;
        Console.WriteLine($"   Deployed validator at {DeployUtxoTxHash}#{DeployUtxoIndex}");

        // Wait for indexing
        await Task.Delay(5000).ConfigureAwait(false);
    }
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// PHASE 1: CREATE ORDER
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
Console.WriteLine();
Console.WriteLine("══ PHASE 1: CREATE ORDER ══");

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

Console.WriteLine($"4. Building create order transaction ({mode})...");

IValue createOutputValue = CreateMultiAssetValue(MinAdaInScript, usdmPolicyId, usdmAssetName, OrderUsdmAmount);

Stopwatch sw = Stopwatch.StartNew();
ITransaction createUnsigned;

if (mode == "MID")
{
    List<ResolvedInput> walletUtxos = await provider.GetUtxosAsync([walletBech32]).ConfigureAwait(false);
    ulong createSlot = await GetCurrentSlot(bfClient).ConfigureAwait(false);
    PostMaryTransaction tx = await new TxBuilder(provider)
        .AddUnspentOutputs(walletUtxos)
        .SetChangeAddress(walletBech32)
        .LockAssets(scriptAddress, createOutputValue, orderDatum)
        .AddMetadata(674, CreateMsgMetadatum("Chrysalis E2E: create order"))
        .SetValidUntil(createSlot + 300)
        .Complete().ConfigureAwait(false);
    createUnsigned = tx;
}
else if (mode == "LOW")
{
    List<ResolvedInput> walletUtxos = await provider.GetUtxosAsync([walletBech32]).ConfigureAwait(false);
    ProtocolParams pparams = await provider.GetParametersAsync().ConfigureAwait(false);

    TransactionBuilder lowBuilder = TransactionBuilder.Create(pparams);
    foreach (ResolvedInput utxo in walletUtxos)
    {
        _ = lowBuilder.AddInput(utxo.Outref);
    }

    _ = lowBuilder
        .AddOutput(scriptAddress, createOutputValue, orderDatum)
        .AddMetadata(674, CreateMsgMetadatum("Chrysalis E2E: create order"))
        .SetFee(0);

    _ = lowBuilder.CalculateFee([], 0, 1, walletUtxos,
        changeAddress: walletBech32, resolvedInputs: walletUtxos);

    createUnsigned = lowBuilder.Build();
}
else
{
    TransactionTemplate<CreateOrderParams> createTemplate =
        TransactionTemplateBuilder.Create<CreateOrderParams>(provider)
            .SetChangeAddress(walletBech32)
            .AddOutput((options, _, _) =>
            {
                options.To = "contract";
                options.Amount = createOutputValue;
                options.SetDatum(orderDatum);
            })
            .AddMetadata(_ => CreateMetadata("Chrysalis E2E: create order"))
            .Build();

    createUnsigned = await createTemplate(new CreateOrderParams(
        walletBech32, MinAdaInScript, PriceNum, PriceDen, scriptAddress, walletBech32)).ConfigureAwait(false);
}

sw.Stop();
Console.WriteLine($"   Built ({sw.ElapsedMilliseconds}ms)");

string createTxId = await SubmitAndConfirm(provider, bfClient, createUnsigned, paymentKey, "create order", 5, cardanoscanBase).ConfigureAwait(false);

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// PHASE 2: FILL ORDER
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
Console.WriteLine();
Console.WriteLine("══ PHASE 2: FILL ORDER ══");

Console.WriteLine("7. Waiting for script UTxO indexing...");
ResolvedInput ourUtxo = await WaitForScriptUtxo(provider, scriptAddress, createTxId).ConfigureAwait(false)
    ?? throw new InvalidOperationException($"Could not find UTxO {createTxId} at script address!");
Console.WriteLine($"   Found: {createTxId}#{ourUtxo.Outref.Index}");

PostAlonzoTransactionOutput ourOutput = (PostAlonzoTransactionOutput)ourUtxo.Output;
InlineDatumOption ourInlineDatum = (InlineDatumOption)ourOutput.Datum!;
byte[] datumCbor = ourInlineDatum.Data.GetValue();

Dictionary<string, ulong> originalAssets = ExtractAssets(ourOutput.Amount);
ulong originalUsdm = originalAssets.GetValueOrDefault(UsdmUnit, 0UL);
if (originalUsdm == 0)
{
    throw new InvalidOperationException("No USDM found in script UTxO!");
}

ulong amountToBuy = originalUsdm * FillPercent / 100;

TemplateWizardDatum parsedDatum = CborSerializer.Deserialize<TemplateWizardDatum>(datumCbor);
byte[] reserializedDatum = CborSerializer.Serialize(parsedDatum);
Console.WriteLine($"   Datum round-trip: {(reserializedDatum.AsSpan().SequenceEqual(datumCbor) ? "OK" : "MISMATCH")}");
Console.WriteLine($"   Original USDM: {originalUsdm}, buying {amountToBuy} ({FillPercent}%)");

ulong currentSlot = await GetCurrentSlot(bfClient).ConfigureAwait(false);
Console.WriteLine($"   Current slot: {currentSlot}");

IValue continuingValue = CalculateNewValue(originalAssets, UsdmUnit, amountToBuy, PriceNum, PriceDen);
TemplateWizardDatum continuingDatum = CborSerializer.Deserialize<TemplateWizardDatum>(datumCbor);
BuyRedeemer fillRedeemerData = new(0, new PlutusTrue(), new None<OracleFeeds>());

Console.WriteLine($"8. Building fill order transaction ({mode})...");
sw.Restart();
ITransaction fillUnsigned;

if (mode == "MID")
{
    List<ResolvedInput> walletUtxos = await WaitForWalletUtxos(provider, walletBech32, createTxId).ConfigureAwait(false);
    ResolvedInput deployRef = await GetDeployRef(provider, deployAddress, DeployUtxoTxHash!, DeployUtxoIndex).ConfigureAwait(false);

    PostMaryTransaction tx = await new TxBuilder(provider)
        .AddUnspentOutputs(walletUtxos)
        .SetChangeAddress(walletBech32)
        .AddReferenceInput(deployRef)
        .AddInput(ourUtxo, fillRedeemerData)
        .AddRequiredSigner(paymentKeyHashHex)
        .LockAssets(scriptAddress, continuingValue, continuingDatum)
        .SetValidFrom(currentSlot)
        .SetValidUntil(currentSlot + 300)
        .AddMetadata(674, CreateMsgMetadatum("Chrysalis E2E: fill order"))
        .Complete().ConfigureAwait(false);
    fillUnsigned = tx;
}
else if (mode == "LOW")
{
    List<ResolvedInput> walletUtxos = await WaitForWalletUtxos(provider, walletBech32, createTxId).ConfigureAwait(false);
    ResolvedInput deployRef = await GetDeployRef(provider, deployAddress, DeployUtxoTxHash!, DeployUtxoIndex).ConfigureAwait(false);

    PostAlonzoTransactionOutput deployOutput = (PostAlonzoTransactionOutput)deployRef.Output;
    IScript validatorScript = deployOutput.ScriptRef!.Deserialize<IScript>();
    ProtocolParams pparams = await provider.GetParametersAsync().ConfigureAwait(false);

    IPlutusData fillPlutusData = CborSerializer.Deserialize<IPlutusData>(CborSerializer.Serialize(fillRedeemerData));
    InputBuilderResult scriptInputResult = new InputBuilder(ourUtxo.Outref, ourUtxo.Output)
        .PlutusScriptRef(validatorScript.HashHex(), fillPlutusData, null, paymentKeyHashHex);

    TransactionBuilder lowBuilder = TransactionBuilder.Create(pparams)
        .AddInput(scriptInputResult)
        .AddReferenceInput(deployRef.Outref)
        .AddOutput(scriptAddress, continuingValue, continuingDatum)
        .SetValidityIntervalStart(currentSlot)
        .SetTtl(currentSlot + 300)
        .AddMetadata(674, CreateMsgMetadatum("Chrysalis E2E: fill order"))
        .SetFee(0);

    // Evaluate BEFORE adding wallet inputs (so redeemer index 0 = script input)
    _ = lowBuilder.Evaluate([ourUtxo, deployRef, .. walletUtxos],
        SlotNetworkConfig.FromNetworkType(provider.NetworkType));

    foreach (ResolvedInput utxo in walletUtxos)
    {
        _ = lowBuilder.AddInput(utxo.Outref);
    }

    _ = lowBuilder.CalculateFee([validatorScript], 0, 1, walletUtxos,
        changeAddress: walletBech32, resolvedInputs: [ourUtxo, .. walletUtxos]);

    fillUnsigned = lowBuilder.Build();
}
else
{
    FillOrderParams fillParams = new(createTxId, ourUtxo.Outref.Index, scriptAddress,
        datumCbor, amountToBuy, PriceNum, PriceDen, DeployUtxoTxHash, DeployUtxoIndex, deployAddress, walletBech32);

    TransactionTemplate<FillOrderParams> fillTemplate =
        TransactionTemplateBuilder.Create<FillOrderParams>(provider)
            .SetChangeAddress(walletBech32)
            .AddReferenceInput((options, param) =>
            {
                options.UtxoRef = TransactionInput.Create(
                    Convert.FromHexString(param.DeployUtxoTxHash), param.DeployUtxoIndex);
                options.Id = "deployRef";
            })
            .AddInput((options, param) =>
            {
                options.UtxoRef = TransactionInput.Create(
                    Convert.FromHexString(param.ScriptUtxoTxHash), param.ScriptUtxoIndex);
                options.Id = "scriptInput";
                options.RedeemerBuilder = (mapping, _, _) =>
                {
                    (_, Dictionary<string, ulong> outputIndices) = mapping.GetInput("scriptInput");
                    ulong outputIndex = outputIndices.TryGetValue("continuingOutput", out ulong idx) ? idx : 0;
                    BuyRedeemer data = new((long)outputIndex, new PlutusTrue(), new None<OracleFeeds>());
                    return new Redeemer<ICborType>(RedeemerTag.Spend, 0, data, ExUnits.Create(1_000_000, 400_000_000));
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
            .Build(eval: true);

    fillUnsigned = await fillTemplate(fillParams).ConfigureAwait(false);
}

sw.Stop();
Console.WriteLine($"   Built ({sw.ElapsedMilliseconds}ms)");

string fillTxId = await SubmitAndConfirm(provider, bfClient, fillUnsigned, paymentKey, "fill order", 9, cardanoscanBase).ConfigureAwait(false);

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// PHASE 3: CLOSE ORDER
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
Console.WriteLine();
Console.WriteLine("══ PHASE 3: CLOSE ORDER ══");

Console.WriteLine("11. Waiting for fill's continuing UTxO indexing...");
ResolvedInput fillUtxo = await WaitForScriptUtxo(provider, scriptAddress, fillTxId).ConfigureAwait(false)
    ?? throw new InvalidOperationException($"Could not find fill UTxO {fillTxId} at script address!");
Console.WriteLine($"   Found: {fillTxId}#{fillUtxo.Outref.Index}");

Console.WriteLine($"12. Building close order transaction ({mode})...");
CloseRedeemer closeRedeemerData = new();

sw.Restart();
ITransaction closeUnsigned;

if (mode == "MID")
{
    List<ResolvedInput> walletUtxos = await WaitForWalletUtxos(provider, walletBech32, fillTxId).ConfigureAwait(false);
    ResolvedInput deployRef = await GetDeployRef(provider, deployAddress, DeployUtxoTxHash!, DeployUtxoIndex).ConfigureAwait(false);

    PostMaryTransaction tx = await new TxBuilder(provider)
        .AddUnspentOutputs(walletUtxos)
        .SetChangeAddress(walletBech32)
        .AddReferenceInput(deployRef)
        .AddInput(fillUtxo, closeRedeemerData)
        .AddRequiredSigner(paymentKeyHashHex)
        .AddMetadata(674, CreateMsgMetadatum("Chrysalis E2E: close order"))
        .Complete().ConfigureAwait(false);
    closeUnsigned = tx;
}
else if (mode == "LOW")
{
    List<ResolvedInput> walletUtxos = await WaitForWalletUtxos(provider, walletBech32, fillTxId).ConfigureAwait(false);
    ResolvedInput deployRef = await GetDeployRef(provider, deployAddress, DeployUtxoTxHash!, DeployUtxoIndex).ConfigureAwait(false);

    PostAlonzoTransactionOutput deployOutput = (PostAlonzoTransactionOutput)deployRef.Output;
    IScript validatorScript = deployOutput.ScriptRef!.Deserialize<IScript>();
    ProtocolParams pparams = await provider.GetParametersAsync().ConfigureAwait(false);

    IPlutusData closePlutusData = CborSerializer.Deserialize<IPlutusData>(CborSerializer.Serialize(closeRedeemerData));
    InputBuilderResult scriptInputResult = new InputBuilder(fillUtxo.Outref, fillUtxo.Output)
        .PlutusScriptRef(validatorScript.HashHex(), closePlutusData, null, paymentKeyHashHex);

    TransactionBuilder lowBuilder = TransactionBuilder.Create(pparams)
        .AddInput(scriptInputResult)
        .AddReferenceInput(deployRef.Outref)
        .AddRequiredSigner(paymentKeyHashHex)
        .AddMetadata(674, CreateMsgMetadatum("Chrysalis E2E: close order"))
        .SetFee(0);

    // Evaluate BEFORE adding wallet inputs
    _ = lowBuilder.Evaluate([fillUtxo, deployRef, .. walletUtxos],
        SlotNetworkConfig.FromNetworkType(provider.NetworkType));

    foreach (ResolvedInput utxo in walletUtxos)
    {
        _ = lowBuilder.AddInput(utxo.Outref);
    }

    _ = lowBuilder.CalculateFee([validatorScript], 0, 1, walletUtxos,
        changeAddress: walletBech32, resolvedInputs: [fillUtxo, .. walletUtxos]);

    closeUnsigned = lowBuilder.Build();
}
else
{
    TransactionTemplate<CloseOrderParams> closeTemplate =
        TransactionTemplateBuilder.Create<CloseOrderParams>(provider)
            .SetChangeAddress(walletBech32)
            .AddRequiredSigner("owner")
            .AddReferenceInput((options, param) =>
            {
                options.UtxoRef = TransactionInput.Create(
                    Convert.FromHexString(param.DeployUtxoTxHash), param.DeployUtxoIndex);
                options.Id = "deployRef";
            })
            .AddInput((options, param) =>
            {
                options.UtxoRef = TransactionInput.Create(
                    Convert.FromHexString(param.ScriptUtxoTxHash), param.ScriptUtxoIndex);
                options.Id = "scriptInput";
                options.RedeemerBuilder = (_, _, _) =>
                    new Redeemer<ICborType>(RedeemerTag.Spend, 0, new CloseRedeemer(), ExUnits.Create(500_000, 200_000_000));
            })
            .AddMetadata(_ => CreateMetadata("Chrysalis E2E: close order"))
            .Build(eval: true);

    closeUnsigned = await closeTemplate(new CloseOrderParams(
        fillTxId, fillUtxo.Outref.Index, scriptAddress, DeployUtxoTxHash, DeployUtxoIndex,
        deployAddress, walletBech32, walletBech32)).ConfigureAwait(false);
}

sw.Stop();
Console.WriteLine($"   Built ({sw.ElapsedMilliseconds}ms)");

string closeTxId = await SubmitAndConfirm(provider, bfClient, closeUnsigned, paymentKey, "close order", 13, cardanoscanBase).ConfigureAwait(false);

// ── Done ────────────────────────────────────────────────────────────────────
Console.WriteLine();
Console.WriteLine("══════════════════════════════════════════════════");
Console.WriteLine("=== SUCCESS: Create → Fill → Close E2E confirmed! ===");
Console.WriteLine($"  Create TX: {createTxId}");
Console.WriteLine($"  Fill TX:   {fillTxId}");
Console.WriteLine($"  Close TX:  {closeTxId}");
Console.WriteLine("══════════════════════════════════════════════════");

return 0;

// ── Shared Helpers ──────────────────────────────────────────────────────────

// Submit, sign, print, confirm — shared across all phases
static async Task<string> SubmitAndConfirm(
    ICardanoDataProvider provider, HttpClient bfClient,
    ITransaction unsigned, PrivateKey key, string label, int stepNum,
    string explorerBase = "https://preview.cardanoscan.io")
{
    ITransaction signed = unsigned.Sign(key);
    byte[] cbor = CborSerializer.Serialize(signed);
    Console.WriteLine($"   Signed TX: {cbor.Length} bytes");

    Console.WriteLine($"{stepNum}. Submitting {label}...");
    Stopwatch sw = Stopwatch.StartNew();
    string txId = await provider.SubmitTransactionAsync(signed).ConfigureAwait(false);
    sw.Stop();
    Console.WriteLine($"   TxHash: {txId} ({sw.ElapsedMilliseconds}ms)");
    Console.WriteLine($"   {explorerBase}/transaction/{txId}");

    Console.WriteLine($"{stepNum + 1}. Waiting for confirmation...");
    bool confirmed = await WaitForConfirmations(bfClient, txId).ConfigureAwait(false);
    if (!confirmed)
    {
        throw new InvalidOperationException($"   {label} timed out!");
    }
    Console.WriteLine("   Confirmed!");
    return txId;
}

// Poll wallet UTxOs until a specific tx's change output appears
static async Task<List<ResolvedInput>> WaitForWalletUtxos(
    ICardanoDataProvider provider, string walletBech32, string expectedTxId)
{
    for (int wait = 0; wait < 60; wait += 4)
    {
        List<ResolvedInput> utxos = await provider.GetUtxosAsync([walletBech32]).ConfigureAwait(false);
        if (utxos.Any(u => Convert.ToHexStringLower(u.Outref.TransactionId.Span) == expectedTxId))
        {
            return utxos;
        }
        await Task.Delay(4000).ConfigureAwait(false);
        Console.Write(".");
    }
    throw new InvalidOperationException($"Wallet UTxO index did not update for tx {expectedTxId}");
}

// Get the deploy reference UTxO
static async Task<ResolvedInput> GetDeployRef(ICardanoDataProvider prov,
    string deployAddress, string deployTxHash, ulong deployIndex)
{
    List<ResolvedInput> deployUtxos = await prov.GetUtxosAsync([deployAddress]).ConfigureAwait(false);
    return deployUtxos.First(u =>
        Convert.ToHexStringLower(u.Outref.TransactionId.Span) == deployTxHash && u.Outref.Index == deployIndex);
}

static IValue CreateMultiAssetValue(ulong lovelace, byte[] policyId, byte[] assetName, ulong amount)
{
    Dictionary<ReadOnlyMemory<byte>, ulong> tokenBundle = new(ReadOnlyMemoryComparer.Instance)
    {
        [(ReadOnlyMemory<byte>)assetName] = amount
    };
    Dictionary<ReadOnlyMemory<byte>, TokenBundleOutput> multiAsset = new(ReadOnlyMemoryComparer.Instance)
    {
        [(ReadOnlyMemory<byte>)policyId] = TokenBundleOutput.Create(tokenBundle)
    };
    return LovelaceWithMultiAsset.Create(lovelace, MultiAssetOutput.Create(multiAsset));
}

static Dictionary<string, ulong> ExtractAssets(IValue value)
{
    Dictionary<string, ulong> assets = [];
    if (value is Lovelace l)
    {
        assets["lovelace"] = l.Amount;
    }
    else if (value is LovelaceWithMultiAsset m)
    {
        assets["lovelace"] = m.Amount;
        foreach ((ReadOnlyMemory<byte> policyId, TokenBundleOutput bundle) in m.MultiAsset.Value)
        {
            foreach ((ReadOnlyMemory<byte> assetName, ulong qty) in bundle.Value)
            {
                assets[Convert.ToHexStringLower(policyId.Span) + Convert.ToHexStringLower(assetName.Span)] = qty;
            }
        }
    }
    return assets;
}

static IValue CalculateNewValue(
    Dictionary<string, ulong> originalAssets, string assetToBuy,
    ulong amountToBuy, long priceNum, long priceDen)
{
    BigInteger sellAmount = ((new BigInteger(amountToBuy) * priceNum) + priceDen - 1) / priceDen;
    Dictionary<string, ulong> newAssets = new(originalAssets);

    ulong currentBuy = newAssets.GetValueOrDefault(assetToBuy, 0UL);
    if (amountToBuy >= currentBuy) { _ = newAssets.Remove(assetToBuy); }
    else { newAssets[assetToBuy] = currentBuy - amountToBuy; }

    newAssets["lovelace"] = newAssets.GetValueOrDefault("lovelace", 0UL) + (ulong)sellAmount;
    return CreateValueFromAssets(newAssets);
}

static IValue CreateValueFromAssets(Dictionary<string, ulong> assets)
{
    ulong lovelace = assets.GetValueOrDefault("lovelace", 0UL);
    Dictionary<string, Dictionary<string, ulong>> byPolicy = [];
    foreach ((string unit, ulong qty) in assets)
    {
        if (unit == "lovelace" || qty == 0) { continue; }
        string policyHex = unit[..56];
        string nameHex = unit.Length > 56 ? unit[56..] : "";
        if (!byPolicy.TryGetValue(policyHex, out Dictionary<string, ulong>? tokens))
        {
            tokens = [];
            byPolicy[policyHex] = tokens;
        }
        tokens[nameHex] = qty;
    }

    if (byPolicy.Count == 0) { return Lovelace.Create(lovelace); }

    Dictionary<ReadOnlyMemory<byte>, TokenBundleOutput> multiAsset = new(ReadOnlyMemoryComparer.Instance);
    foreach ((string policyHex, Dictionary<string, ulong> tokens) in byPolicy)
    {
        Dictionary<ReadOnlyMemory<byte>, ulong> bundle = new(ReadOnlyMemoryComparer.Instance);
        foreach ((string nameHex, ulong qty) in tokens) { bundle[Convert.FromHexString(nameHex)] = qty; }
        multiAsset[Convert.FromHexString(policyHex)] = TokenBundleOutput.Create(bundle);
    }
    return LovelaceWithMultiAsset.Create(lovelace, MultiAssetOutput.Create(multiAsset));
}

static ITransactionMetadatum CreateMsgMetadatum(string message) =>
    MetadatumMap.Create(new Dictionary<ITransactionMetadatum, ITransactionMetadatum>
    {
        { MetadataText.Create("msg"), MetadatumList.Create([MetadataText.Create(message)]) }
    });

static Metadata CreateMetadata(string message) =>
    Metadata.Create(new Dictionary<ulong, ITransactionMetadatum>
    {
        { 674, CreateMsgMetadatum(message) }
    });

static async Task<bool> WaitForConfirmations(HttpClient client, string txHash,
    int requiredConfirmations = 1, int maxWaitSeconds = 240, int pollSeconds = 5)
{
    Stopwatch sw = Stopwatch.StartNew();
    for (int elapsed = 0; elapsed < maxWaitSeconds; elapsed += pollSeconds)
    {
        await Task.Delay(TimeSpan.FromSeconds(pollSeconds)).ConfigureAwait(false);
        using HttpResponseMessage response = await client
            .GetAsync(new Uri($"txs/{txHash}", UriKind.Relative)).ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            ulong confirmations = 0;
            if (root.TryGetProperty("block", out JsonElement block) &&
                block.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(block.GetString()))
            {
                confirmations = 1;
            }
            if (confirmations >= (ulong)requiredConfirmations)
            {
                sw.Stop();
                Console.Write($" ({sw.ElapsedMilliseconds / 1000}s, {confirmations} conf)");
                return true;
            }
        }
        Console.Write(".");
    }
    Console.WriteLine();
    return false;
}

static async Task<ResolvedInput?> WaitForScriptUtxo(
    ICardanoDataProvider provider, string scriptAddress, string txHash,
    int maxWaitSeconds = 120, int pollSeconds = 4)
{
    for (int elapsed = 0; elapsed < maxWaitSeconds; elapsed += pollSeconds)
    {
        List<ResolvedInput> scriptUtxos = await provider.GetUtxosAsync([scriptAddress]).ConfigureAwait(false);
        foreach (ResolvedInput utxo in scriptUtxos)
        {
            if (Convert.ToHexStringLower(utxo.Outref.TransactionId.Span) == txHash) { return utxo; }
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
        .GetAsync(new Uri("blocks/latest", UriKind.Relative)).ConfigureAwait(false);
    _ = response.EnsureSuccessStatusCode();
    string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    using JsonDocument doc = JsonDocument.Parse(json);
    return doc.RootElement.GetProperty("slot").GetUInt64();
}
