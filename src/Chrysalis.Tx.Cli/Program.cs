using System.Transactions;
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
string validatorAddress = "addr_test1wq37rysxtj0ctsgz40ds3r3x3mt8csywcf0tk9w2gc5tjqc2dup70";

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

var lockLovelace = TxTemplateBuilder<LockParameters>.Create(provider)
    .AddStaticParty("rico", ricoAddress)
    .AddStaticParty("validator", validatorAddress)
    .AddInput((options, lockParams) =>
    {
        options.From = "rico";
        options.MinAmount = lockParams.Amount;
    })
    .AddOutput((options, lockParams) =>
    {
        options.To = "validator";
        options.Amount = lockParams.Amount;
        options.Datum = lockParams.Datum;
    })
    .Build();

// var lockParams = new LockParameters(new Lovelace(10000000), new InlineDatumOption(new CborInt(1), new CborEncodedValue(Convert.FromHexString("d87980"))));
// var unsignedLockTx = await lockLovelace(lockParams);
// Console.WriteLine(Convert.ToHexString(CborSerializer.Serialize(unsignedLockTx)));

string scriptRefTxHash = "9489655981e70ab2c2df5db10d2ed11157bc2e404d02c3fabe853737366ced77";
string lockTxHash = "ffe4c125de5d60d07413b803cc858b146c3831d44cdd81b18ee785e94fe7e43c";
string withdrawalAddress = "stake_test17q37rysxtj0ctsgz40ds3r3x3mt8csywcf0tk9w2gc5tjqc29zef9";


var unlockLovelace = TxTemplateBuilder<UnlockParameters>.Create(provider)
    .AddStaticParty("rico", ricoAddress)
    .AddStaticParty("validator", validatorAddress)
    .AddStaticParty("withdrawal", withdrawalAddress)
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
    .AddWithdrawal((options, unlockParams) =>
    {
        options.From = "withdrawal";
        options.Amount = unlockParams.WithdrawalAmount;
        options.Redeemer = unlockParams.WithdrawRedeemer;
    })
    .Build();

var spendRedeemerKey = new RedeemerKey(new CborInt(0), new CborUlong(0));
var spendRedeemerValue = new RedeemerValue(new PlutusConstr([]), new ExUnits(new CborUlong(14000000), new CborUlong(10000000000)));

var withdrawRedeemerKey = new RedeemerKey(new CborInt(3), new CborUlong(0));
var withdrawRedeemerValue = new RedeemerValue(new PlutusConstr([]), new ExUnits(new CborUlong(14000000), new CborUlong(10000000000)));

UnlockParameters unlockParams = new(
    new TransactionInput(new CborBytes(Convert.FromHexString(lockTxHash)), new CborUlong(0)),
    new TransactionInput(new CborBytes(Convert.FromHexString(scriptRefTxHash)), new CborUlong(0)),
    new RedeemerMap(new Dictionary<RedeemerKey, RedeemerValue> { { spendRedeemerKey, spendRedeemerValue } }),
    new Lovelace(20000000),
    0,
   new RedeemerMap(new Dictionary<RedeemerKey, RedeemerValue> { { withdrawRedeemerKey, withdrawRedeemerValue } })
);

var unlockUnsignedTx = await unlockLovelace(unlockParams);
Console.WriteLine(Convert.ToHexString(CborSerializer.Serialize(unlockUnsignedTx)));