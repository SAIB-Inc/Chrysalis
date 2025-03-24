using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Input;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Output;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Protocol;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Script;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.WitnessSet;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types.Primitives;
using Chrysalis.Tx.Cli;
using Chrysalis.Tx.Provider;
using Chrysalis.Tx.TemplateBuilder;

// byte[] sendLovelaceSignedTx = await SampleTransactions.SendLovelaceAsync();
// Console.WriteLine(Convert.ToHexString(sendLovelaceSignedTx));

// byte[] signedTx = await SampleTransactions
//     .UnlockLovelaceAsync();
// Console.WriteLine(Convert.ToHexString(signedTx));


var provider = new Blockfrost("previewajMhMPYerz9Pd3GsqjayLwP5mgnNnZCC");
string ricoAddress = "addr_test1qpw9cvvdq8mjncs9e90trvpdvg7azrncafv0wtgvz0uf9vhgjp8dc6v79uxw0detul8vnywlv5dzyt32ayjyadvhtjaqyl2gur";
string validatorAddress = "addr_test1wrf8enqnl26m0q5cfg73lxf4xxtu5x5phcrfjs0lcqp7uagh2hm3k";

// var transfer = TxTemplateBuilder<ulong>.Create(provider)
//     .AddStaticParty("rico", ricoAddress)
//     .AddStaticParty("rico", ricoAddress)
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
// Console.WriteLine(Convert.ToHexString(CborSerializer.Serialize(unsignedTx)));

// var lockLovelace = TxTemplateBuilder<LockParameters>.Create(provider)
//     .AddStaticParty("rico", ricoAddress)
//     .AddStaticParty("validator", validatorAddress)
//     .AddInput((options, lockParams) =>
//     {
//         options.From = "rico";
//         options.MinAmount = lockParams.Amount;
//     })
//     .AddOutput((options, lockParams) =>
//     {
//         options.To = "validator";
//         options.Amount = lockParams.Amount;
//         options.Datum = lockParams.Datum;
//     })
//     .Build();

// var lockParams = new LockParameters(new Lovelace(10000000), new InlineDatumOption(new CborInt(1), new CborEncodedValue(Convert.FromHexString("d87980"))));
// var unsignedLockTx = await lockLovelace(lockParams);
// Console.WriteLine(Convert.ToHexString(CborSerializer.Serialize(unsignedLockTx)));

string scriptRefTxHash = "6ba7ea1e216dfc0d47ae9d5ba2045acffdc52c592a0f1efd1ad5dedf4bdc8cea";
string lockTxHash = "997f2595886bc065eb7dd2e0a0a0d63bbf6399f250d62cd467cecf25d4353a69";

var unlockLovelace = TxTemplateBuilder<UnlockParameters>.Create(provider)
    .AddStaticParty("rico", ricoAddress)
    .AddStaticParty("validator", validatorAddress)
    .AddInput((options, unlockParams) =>
    {
        options.From = "validator";
        options.UtxoRef = unlockParams.ScriptRefUtxoOutref;
        options.IsReference = true;
    })
    .AddInput((options, unlockParams) =>
    {
        options.From = "validator";
        options.UtxoRef = unlockParams.LockedUtxoOutRef;
        options.Redeemer = unlockParams.Redeemer;
    })
    .AddOutput((options, unlockParams) =>
    {
        options.To = "rico";
        options.Amount = unlockParams.Amount;
    })
    .Build();

var redeemerKey = new RedeemerKey(new CborInt(0), new CborUlong(0));
var redeemerValue = new RedeemerValue(new PlutusConstr([]), new ExUnits(new CborUlong(1000000), new CborUlong(1000000)));

UnlockParameters unlockParams = new(
    new TransactionInput(new CborBytes(Convert.FromHexString(lockTxHash)), new CborUlong(0)),
    new TransactionInput(new CborBytes(Convert.FromHexString(scriptRefTxHash)), new CborUlong(0)),
    new RedeemerMap(new Dictionary<RedeemerKey, RedeemerValue> { { redeemerKey, redeemerValue } }),
    new Lovelace(10000000)
);

var unlockUnsignedTx = await unlockLovelace(unlockParams);
Console.WriteLine(Convert.ToHexString(CborSerializer.Serialize(unlockUnsignedTx)));