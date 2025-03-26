using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Protocol;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Cbor.Types.Cardano.Core.TransactionWitness;
using Chrysalis.Tx.Cli;
using Chrysalis.Tx.Extensions;
using Chrysalis.Tx.Provider;
using Chrysalis.Tx.TransactionBuilding;
using Chrysalis.Tx.Words;
using Chrysalis.Wallet.Extensions;
using Chrysalis.Wallet.Keys;
using Chrysalis.Wallet.Models.Enums;

// byte[] sendLovelaceSignedTx = await SampleTransactions.SendLovelaceAsync();
// Console.WriteLine(Convert.ToHexString(sendLovelaceSignedTx));

// byte[] signedTx = await SampleTransactions
//     .UnlockLovelaceAsync();
// Console.WriteLine(Convert.ToHexString(signedTx));

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


var provider = new Blockfrost("previewajMhMPYerz9Pd3GsqjayLwP5mgnNnZCC");
string ricoAddress = "addr_test1qpw9cvvdq8mjncs9e90trvpdvg7azrncafv0wtgvz0uf9vhgjp8dc6v79uxw0detul8vnywlv5dzyt32ayjyadvhtjaqyl2gur";
string validatorAddress = "addr_test1wrffnmkn0pds0tmka6lsce88l5c9mtd90jv2u2vkfguu3rg7k7a60";

// var transfer = TransactionTemplateBuilder<ulong>.Create(provider)
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

// var lockLovelace = TransactionTemplateBuilder<LockParameters>.Create(provider)
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

// var lockParams = new LockParameters(new Lovelace(10000000), new InlineDatumOption(1, new CborEncodedValue(Convert.FromHexString("d87980"))));
// var unsignedLockTx = await lockLovelace(lockParams);
// Console.WriteLine(Convert.ToHexString(CborSerializer.Serialize(unsignedLockTx)));
// Console.WriteLine(Convert.ToHexString(await SampleTransactions.SendLovelaceAsync()));


Action<Dictionary<int, Dictionary<string, int>>, UnlockParameters, Dictionary<RedeemerKey, RedeemerValue>> redeemerBuilder =
    (inputOutputAssosciations, parameters, redeemers) =>
    {
        List<PlutusData> actions = new();
        foreach (var assoc in inputOutputAssosciations)
        {
            List<PlutusData> outputIndicesData = [];
            foreach (var outputIndex in assoc.Value)
            {
                outputIndicesData.Add(new PlutusInt64(outputIndex.Value));
            }
            PlutusConstr outputIndicesPlutusData = new(outputIndicesData)
            {
                ConstrIndex = 121
            };
            PlutusConstr actionPlutusData = new([new PlutusInt64(assoc.Key), outputIndicesPlutusData])
            {
                ConstrIndex = 121
            };
            actions.Add(actionPlutusData);
        }
        PlutusList withdrawRedeemer = new(actions)
        {
            ConstrIndex = 121
        };
        redeemers.Add(new RedeemerKey(3, 0), new RedeemerValue(withdrawRedeemer, new ExUnits(140000, 100000000)));
    };

string scriptRefTxHash = "54ffbc45dd2518ca808f16e516e9521023a546625ebd3d9047c5f98f312b5c4e";
string lockTxHash = "7822c1fade8578c11aa95432f8c1c143b562fe14a65a76e2de372661f9a4a958";
string withdrawalAddress = "stake_test17rffnmkn0pds0tmka6lsce88l5c9mtd90jv2u2vkfguu3rg77q9d9";

var unlockLovelace = TransactionTemplateBuilder<UnlockParameters>.Create(provider)
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
        options.Id = "borrow";
    })
    .AddOutput((options, unlockParams) =>
    {
        options.To = "rico";
        options.Amount = unlockParams.MainAmount;
        options.Datum = unlockParams.MainDatum;
        options.AssociatedInputId = "borrow";
        options.Id = "main";
    })
    .AddOutput((options, unlockParams) =>
    {
        options.To = "rico";
        options.Amount = unlockParams.FeeAmount;
        options.Datum = unlockParams.FeeDatum;
        options.AssociatedInputId = "borrow";
        options.Id = "fee";
    })
    .AddOutput((options, unlockParams) =>
    {
        options.To = "rico";
        options.Amount = unlockParams.ChangeAmount;
        options.Datum = unlockParams.ChangeDatum;
        options.AssociatedInputId = "borrow";
        options.Id = "change";
    })
    .AddWithdrawal((options, unlockParams) =>
    {
        options.From = "withdrawal";
        options.Amount = unlockParams.WithdrawalAmount;
        options.RedeemerGenerator = redeemerBuilder;
    })
    .Build();


PlutusConstr plutusConstr = new([])
{
    ConstrIndex = 121
};

var spendRedeemerKey = new RedeemerKey(0, 0);
var spendRedeemerValue = new RedeemerValue(plutusConstr, new ExUnits(140000, 100000000));

var withdrawRedeemerKey = new RedeemerKey(3, 0);

var withdrawRedeemerValue = new RedeemerValue(plutusConstr, new ExUnits(14000000, 10000000000));

PlutusData withdrawRedeemer = CborSerializer.Deserialize<PlutusData>(Convert.FromHexString("D8799FD8799F01D8799F000102FFFFFF"));

UnlockParameters unlockParams = new(
    new TransactionInput(Convert.FromHexString(lockTxHash), 0),
    new TransactionInput(Convert.FromHexString(scriptRefTxHash), 0),
    new RedeemerMap(new Dictionary<RedeemerKey, RedeemerValue> { { spendRedeemerKey, spendRedeemerValue } }),
    new Lovelace(20000000),
    new Lovelace(10000000),
    new Lovelace(5000000),
    0,
    new InlineDatumOption(1, new CborEncodedValue(Convert.FromHexString("446D61696E"))),
    new InlineDatumOption(1, new CborEncodedValue(Convert.FromHexString("43666565"))),
    new InlineDatumOption(1, new CborEncodedValue(Convert.FromHexString("466368616E6765"))),
   new RedeemerMap(new Dictionary<RedeemerKey, RedeemerValue> { { withdrawRedeemerKey, withdrawRedeemerValue } })
);


PostMaryTransaction unlockUnsignedTx = await unlockLovelace(unlockParams);
PostMaryTransaction unlockSignedTx = unlockUnsignedTx.Sign(privateKey);
Console.WriteLine(Convert.ToHexString(CborSerializer.Serialize(unlockSignedTx)));

