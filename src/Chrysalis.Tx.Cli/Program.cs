using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Protocol;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Cbor.Types.Cardano.Core.TransactionWitness;
using Chrysalis.Tx.Builders;
using Chrysalis.Tx.Cli;
using Chrysalis.Tx.Extensions;
using Chrysalis.Tx.Providers;
using Chrysalis.Wallet.Models.Enums;
using Chrysalis.Wallet.Models.Keys;
using Chrysalis.Wallet.Words;

// byte[] sendLovelaceSignedTx = await SampleTransactions.SendLovelaceAsync();
// Console.WriteLine(Convert.ToHexString(sendLovelaceSignedTx));

// byte[] signedTx = await SampleTransactions
//     .UnlockLovelaceAsync();
// Console.WriteLine(Convert.ToHexString(signedTx));

string words = "";

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
//     .AddStaticParty("rico", ricoAddress, true)
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
        List<PlutusData> actions = [];
        List<ulong> spendIndices = [];
        foreach (var assoc in inputOutputAssosciations)
        {
            List<PlutusData> outputIndicesData = [];
            spendIndices.Add((ulong)assoc.Key);
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

        PlutusConstr emptyConstr = new([])
        {
            ConstrIndex = 121
        };

        foreach (var inputIndex in spendIndices)
        {
            redeemers.Add(new RedeemerKey(0, inputIndex), new RedeemerValue(emptyConstr, new ExUnits(1400000, 100000000)));
        }

        redeemers.Add(new RedeemerKey(3, 0), new RedeemerValue(withdrawRedeemer, new ExUnits(1400000, 100000000)));
    };

string scriptRefTxHash = "54ffbc45dd2518ca808f16e516e9521023a546625ebd3d9047c5f98f312b5c4e";
string lockTxHash = "f4103fddda67074659c43cdbd3b59fb75842cec9818c2f745e471a22e191ec6d";
string withdrawalAddress = "stake_test17rffnmkn0pds0tmka6lsce88l5c9mtd90jv2u2vkfguu3rg77q9d9";

var unlockLovelace = TransactionTemplateBuilder<UnlockParameters>.Create(provider)
    .AddStaticParty("rico", ricoAddress, true)
    .AddStaticParty("validator", validatorAddress)
    .AddStaticParty("withdrawal", withdrawalAddress)
    .SetRedeemerBuilder(redeemerBuilder)
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
    })
    .Build();




UnlockParameters unlockParams = new(
    new TransactionInput(Convert.FromHexString(lockTxHash), 0),
    new TransactionInput(Convert.FromHexString(scriptRefTxHash), 0),
    null,
    new Lovelace(20000000),
    new Lovelace(10000000),
    new Lovelace(5000000),
    0,
    new InlineDatumOption(1, new CborEncodedValue(Convert.FromHexString("446D61696E"))),
    new InlineDatumOption(1, new CborEncodedValue(Convert.FromHexString("43666565"))),
    new InlineDatumOption(1, new CborEncodedValue(Convert.FromHexString("466368616E6765"))),
   null
);


PostMaryTransaction unlockUnsignedTx = await unlockLovelace(unlockParams);
PostMaryTransaction unlockSignedTx = unlockUnsignedTx.Sign(privateKey);
Console.WriteLine(Convert.ToHexString(CborSerializer.Serialize(unlockSignedTx)));

