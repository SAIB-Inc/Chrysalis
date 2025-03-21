using Chrysalis.Tx.TemplateBuilder;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Output;
using Chrysalis.Tx.Provider;
using Chrysalis.Tx.TransactionBuilding;
using Chrysalis.Tx.Utils;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Cardano.Extensions;
using Chrysalis.Tx.Models;
using Chrysalis.Tx.TransactionBuilding.Extensions;
using Chrysalis.Cbor.Types.Primitives;
using Chrysalis.Tx.Models.Keys;
using Chrysalis.Tx.Words;
using Chrysalis.Tx.Extensions;
using Chrysalis.Tx.Models.Enums;
using Chrysalis.Tx.Services.Encoding;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Input;
using Chrysalis.Cbor.Types.Custom;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Script;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.WitnessSet;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Protocol;
using System.Data;



var rico = "005c5c318d01f729e205c95eb1b02d623dd10e78ea58f72d0c13f892b2e8904edc699e2f0ce7b72be7cec991df651a222e2ae9244eb5975cba";
// var bob = "005c5c318d01f729e205c95eb1b02d623dd10e78ea58f72d0c13f892b2e8904edc699e2f0ce7b72be7cec991df651a222e2ae9244eb5975cba";

var provider = new Blockfrost("previewajMhMPYerz9Pd3GsqjayLwP5mgnNnZCC");

byte[] expectedOutput = Convert.FromHexString("616ac02d17482feed0d68115ffee130e528aa4a4dfba6102f55e97eae40e5a09");

string words = "mosquito creek harbor detail change secret design mistake next labor bench mule elite vapor menu hurdle what tobacco improve caught anger aware legal project";

Mnemonic mnemonic = Mnemonic.Restore(words, English.Words);

PrivateKey accountKey = mnemonic
            .GetRootKey()
            .Derive(PurposeType.Shelley, DerivationType.HARD)
            .Derive(CoinType.Ada, DerivationType.HARD)
            .Derive(0, DerivationType.HARD);

PrivateKey privateKey = accountKey
            .Derive(RoleType.ExternalChain)
            .Derive(0);

PublicKey pkPub = privateKey.GetPublicKey();

PublicKey skPub = accountKey
            .Derive(RoleType.Staking)
            .Derive(0)
            .GetPublicKey();

byte[] addressBody = [.. HashUtil.Blake2b224(pkPub.Key), .. HashUtil.Blake2b224(skPub.Key)];
byte header = AddressUtil.GetHeader(AddressUtil.GetNetworkInfo(NetworkType.Testnet), AddressType.BasePayment);
string prefix = AddressUtil.GetPrefix(AddressType.BasePayment, NetworkType.Testnet);

string address = Bech32Codec.Encode([header, .. addressBody], prefix);

string scriptRefTxHash = "6ba7ea1e216dfc0d47ae9d5ba2045acffdc52c592a0f1efd1ad5dedf4bdc8cea";

var utxos = await provider.GetUtxosAsync(address);
var utxos_copy = new List<ResolvedInput>(utxos);
var pparams = await provider.GetParametersAsync();

var txBuilder = TransactionBuilder.Create(pparams);

var scriptAddress = "70d27ccc13fab5b782984a3d1f99353197ca1a81be069941ffc003ee75";
var lockedUtxoTxHash = "ecf14739bdeebb0d324525ec675818ef24693f8d9fc36e85cc7a620eea819245";

var policy = "0401ce1fb4da93ce46ed4a22c95d3502a59c76476649a41e7bafa9da";
var assetName = "494147";
Dictionary<CborBytes, CborUlong> tokenBundle = new(){
    { new CborBytes(Convert.FromHexString(assetName)), new CborUlong(1000) }
};

Dictionary<CborBytes, TokenBundleOutput> multiAsset = new(){
    { new CborBytes(Convert.FromHexString(policy)), new TokenBundleOutput(tokenBundle) }
};

// var output = new AlonzoTransactionOutput(
//     new Address(Convert.FromHexString(rico)),
//     new LovelaceWithMultiAsset(new Lovelace(2000000UL), new MultiAssetOutput(multiAsset)),
//     null
//     );

byte[] scriptBytes = Convert.FromHexString("585c01010029800aba2aba1aab9eaab9dab9a4888896600264653001300600198031803800cc0180092225980099b8748008c01cdd500144c8cc892898050009805180580098041baa0028b200c180300098019baa0068a4d13656400401");

var refInput = new TransactionInput(new CborBytes(Convert.FromHexString(scriptRefTxHash)), new CborUlong(0));
var refOutput = new PostAlonzoTransactionOutput(
    new Address(Convert.FromHexString(scriptAddress)),
    new Lovelace(1301620UL),
    null,
    new CborEncodedValue(scriptBytes)
    );
var refUtxo = new ResolvedInput(refInput, refOutput);

var lockedUtxoOutref = new TransactionInput(new CborBytes(Convert.FromHexString(lockedUtxoTxHash)), new CborUlong(0));

var datum = new InlineDatumOption(new CborInt(1), new CborEncodedValue(Convert.FromHexString("d87980")));

//Todo Implement GetResolvedUtxo in provider
var lockedUtxo = new ResolvedInput(
    lockedUtxoOutref,
    new PostAlonzoTransactionOutput(
        new Address(Convert.FromHexString(scriptAddress)),
         new Lovelace(20000000UL), datum, null));

utxos_copy.Add(lockedUtxo);
utxos_copy.Add(refUtxo);

var output = new PostAlonzoTransactionOutput(
    new Address(Convert.FromHexString(scriptAddress)),
    new Lovelace(20000000UL),
    null,
    null
    );


ResolvedInput? feeInput = null;
foreach (var utxo in utxos.OrderByDescending(e => e.Output.Amount()!.Lovelace()))
{
    if (utxo.Output.Amount()!.Lovelace() >= 5000000UL && utxo.Output.Amount() is Lovelace)
    {
        feeInput = utxo;
        break;
    }
}

if (feeInput is not null)
{
    utxos.Remove(feeInput);
    txBuilder.AddInput(feeInput.Outref);
}

ResolvedInput? collateralInput = null;
foreach (var utxo in utxos.OrderByDescending(e => e.Output.Amount()!.Lovelace()))
{
    if (utxo.Output.Amount()!.Lovelace() >= 5000000UL && utxo.Output.Amount() is Lovelace)
    {
        collateralInput = utxo;
        break;
    }
}

if (collateralInput is not null)
{
    utxos.Remove(collateralInput);
    txBuilder.AddCollateral(collateralInput.Outref);
}


var coinSelectionResult = CoinSelectionAlgorithm.LargestFirstAlgorithm(utxos, [output.Amount]);

foreach (var consumed_input in coinSelectionResult.Inputs)
{
    txBuilder.AddInput(consumed_input.Outref);
}


var lovelaceChange = new Lovelace(coinSelectionResult.LovelaceChange! + (feeInput?.Output.Amount()!.Lovelace() ?? 0 + (collateralInput?.Output.Amount()!.Lovelace() ?? 0)));
Value changeValue = lovelaceChange;

if (coinSelectionResult.AssetsChange.Count > 0)
{
    changeValue = new LovelaceWithMultiAsset(lovelaceChange, new MultiAssetOutput(coinSelectionResult.AssetsChange));
}


var changeOutput = new PostAlonzoTransactionOutput(
    new Address(Convert.FromHexString(rico)),
    changeValue,
    null,
    null
    );

var collateralOutput = new PostAlonzoTransactionOutput(
    new Address(Convert.FromHexString(rico)),
    new Lovelace(collateralInput?.Output.Amount()!.Lovelace() ?? 0),
    null,
    null
    );


// var redeemerKey = new RedeemerKey(new CborInt(0), new CborUlong(0));
// var redeemerValue = new RedeemerValue(new, new ExUnits(new CborUlong(0), new CborUlong(0)));

// var redeemers = CborSerializer.Deserialize<RedeemerMap>(Convert.FromHexString("a182000082d87980821926171a002b49b1"));
var redeemer = new RedeemerEntry(new CborInt(0), new CborUlong(0), CborSerializer.Deserialize<PlutusData>(Convert.FromHexString("d87980")), new ExUnits(new CborUlong(0), new CborUlong(0)));

txBuilder
    .AddReferenceInput(refInput)
    .AddInput(lockedUtxoOutref)
    .AddOutput(output)
    .AddOutput(changeOutput, true)
    .SetCollateralReturn(collateralOutput)
    .SetRedeemers(new RedeemerList([redeemer]))
    // .Evaluate(utxos_copy)
    .CalculateFee(scriptBytes);


var unsignedTx = txBuilder.Build();

var signedTx = unsignedTx
    .Sign(privateKey);

var signedTxHash = CborSerializer.Serialize(signedTx);

Console.WriteLine(Convert.ToHexString(signedTxHash));


//Simple Transfer
// users can use any type as parameters
// var transfer = TxTemplateBuilder<ulong>.Create(provider)
//     .AddStaticParty("rico", rico)
//     .AddInput((options, amount) =>
//     {
//         options.From = "rico";
//         options.MinAmount = new Lovelace(amount);
//     })
//     .AddOutput((options, amount) =>
//     {
//         options.To = "rico";
//         options.Amount = new Lovelace(amount);
//     })
//     .Build();

// var unsignedTx = await transfer(10000000UL);
// Console.WriteLine(Convert.ToHexString(unsignedTx));

// //Simple Transfer with additional parties
// // users can use any type as parameters
// var transferWithAdditionalParites = TxTemplateBuilder<TransferParameters>.Create()
//     .AddStaticParty("alice", alice)
//     .AddStaticParty("bob", bob)
//     .AddInput((options, transferParams) =>
//     {
//         options.From = "alice";
//         options.MinAmount = new Lovelace(transferParams.Amount);
//     })
//     .AddOutput((options, transferParams) =>
//     {
//         options.To = "bob";
//         options.Amount = new Lovelace(transferParams.Amount);
//     })
//     .Build();

// var unsignedTx = transferWithAdditionalParites(
//     new TransferParameters(1000, []));

// var validator = "005c5c318d01f729e205c95eb1b02d623dd10e78ea58f72d0c13f892b2e8904edc699e2f0ce7b72be7cec991df651a222e2ae9244eb5975cba";

// Lock and Unlock
// Users can create their own custom datatypes and use them as parameters
// var lockTx = TxTemplateBuilder<LockParameters>.Create(provider)
//     .AddStaticParty("rico", rico)
//     .AddStaticParty("validator", validator)
//     .AddInput((options, lockParams) =>
//     {
//         options.From = "alice";
//         options.MinAmount = lockParams.Amount;
//     })
//     .AddOutput((options, lockParams) =>
//     {
//         options.To = "validator";
//         options.Amount = lockParams.Amount;
//         options.Datum = lockParams.Datum;
//     })
//     .Build();

// var lockUnSignedTx = lockTx(new LockParameters(new Lovelace(1000000UL), new Datum("hello")));

// var unlockTx = TxTemplateBuilder<UnlockParameters>.Create()
//     .AddStaticParty("alice", alice)
//     .AddStaticParty("validator", validator)
//     .AddInput((options, unlockParams) =>
//     {
//         options.From = "validator";
//         options.UtxoRef = unlockParams.UtxoRef;
//         options.Redeemer = unlockParams.Redeemer;
//     })
//     // Since this is unlock tx the amount is calculated automatically unless the user wants to specify
//     .AddOutput((options, unlockParams) =>
//     {
//         options.To = "alice";
//     })
//     .Build();

// var unlockUnSignedTx = unlockTx(new UnlockParameters("locked_utxo", new Redeemer("hello")));

