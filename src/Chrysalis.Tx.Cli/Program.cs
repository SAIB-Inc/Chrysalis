using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Tx.Builders;
using Chrysalis.Tx.Cli;
using Chrysalis.Tx.Extensions;
using Chrysalis.Tx.Models;
using Chrysalis.Tx.Providers;
using Chrysalis.Wallet.Models.Enums;
using Chrysalis.Wallet.Models.Keys;
using Chrysalis.Wallet.Words;

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
//     .AddStaticParty("rico", ricoAddress, true)
//     .AddInput((options, amount) =>
//     {
//         options.From = "rico";
//     })
//     .AddOutput((options, amount) =>
//     {
//         options.To = "rico";
//         options.Amount = new LovelaceWithMultiAsset(
//             new Lovelace(10000000UL),
//             new MultiAssetOutput(
//                 new Dictionary<byte[], TokenBundleOutput>
//                 {
//                     { Convert.FromHexString("0401ce1fb4da93ce46ed4a22c95d3502a59c76476649a41e7bafa9da"), new TokenBundleOutput(new Dictionary<byte[], ulong>(){
//                         { Convert.FromHexString("494147"), amount }
//                     }) }
//                 }
//             )
//         );
//     })
//     .Build();

// var provider = new Blockfrost("previewajMhMPYerz9Pd3GsqjayLwP5mgnNnZCC");
// string ricoAddress = "addr_test1qpw9cvvdq8mjncs9e90trvpdvg7azrncafv0wtgvz0uf9vhgjp8dc6v79uxw0detul8vnywlv5dzyt32ayjyadvhtjaqyl2gur";

// var transfer = TransactionTemplateBuilder<ulong>.Create(provider)
// .AddStaticParty("rico", ricoAddress, true)
// .AddInput((options, amount) =>
// {
//     options.From = "rico";
// })
// .AddOutput((options, amount) =>
// {
//     options.To = "rico";
//     options.Amount = new Lovelace(amount);

// })
// .Build();

// Transaction unlockUnsignedTx = await transfer(10000000UL);
// Transaction unlockSignedTx = unlockUnsignedTx.Sign(privateKey);
// Console.WriteLine(Convert.ToHexString(CborSerializer.Serialize(unlockSignedTx)));

// Console.WriteLine(Convert.ToHexString(await SampleTransactions.SendLovelaceAsync()));

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




RedeemerDataBuilder<UnlockParameters, CborIndefList<Indices>> withdrawRedeemerBuilder = (mapping, parameters) =>
{
    List<PlutusData> actions = [];
    var (inputIndex, outputIndices) = mapping.GetInput("borrow");

    List<ulong> outputIndicesData = [];
    foreach (var (_, outputIndex) in outputIndices)
    {
        outputIndicesData.Add(outputIndex);
    }
    
    Indices indices = new Indices(
        inputIndex,
        new OutputIndices(outputIndices["main"], outputIndices["fee"], outputIndices["change"])
    );

    return new CborIndefList<Indices>([indices]);
};


RedeemerDataBuilder<UnlockParameters, PlutusData> spendRedeemerBuilder = (mapping, parameters) =>
{
    return new PlutusConstr([])
    {
        ConstrIndex = 121
    };
};

string scriptRefTxHash = "54ffbc45dd2518ca808f16e516e9521023a546625ebd3d9047c5f98f312b5c4e";
string lockTxHash = "6b0232f053d486ce8fe7dc9a8f19e75b4c0443298240dfbfe778f55a4d107006";
string withdrawalAddress = "stake_test17rffnmkn0pds0tmka6lsce88l5c9mtd90jv2u2vkfguu3rg77q9d9";

var unlockLovelace = TransactionTemplateBuilder<UnlockParameters>.Create(provider)
    .AddStaticParty("rico", ricoAddress, true)
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
        options.Id = "borrow";
        options.SetRedeemerBuilder(spendRedeemerBuilder);
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
        options.Id = "withdrawal";
        options.SetRedeemerFactory(withdrawRedeemerBuilder);
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


Transaction unlockUnsignedTx = await unlockLovelace(unlockParams);
// Console.WriteLine(Convert.ToHexString(CborSerializer.Serialize(unlockUnsignedTx)));
Transaction unlockSignedTx = unlockUnsignedTx.Sign(privateKey);
Console.WriteLine(Convert.ToHexString(CborSerializer.Serialize(unlockSignedTx)));



// var provider = new Blockfrost("previewajMhMPYerz9Pd3GsqjayLwP5mgnNnZCC");
// string ricoAddress = "addr_test1qpw9cvvdq8mjncs9e90trvpdvg7azrncafv0wtgvz0uf9vhgjp8dc6v79uxw0detul8vnywlv5dzyt32ayjyadvhtjaqyl2gur";
// string validatorAddress = "addr_test1wrf8enqnl26m0q5cfg73lxf4xxtu5x5phcrfjs0lcqp7uagh2hm3k";

// string scriptRefTxHash = "c2609532a76c3f1d38a9e7192bb32946f9ca8ab47c635d99dffa3a3da7c9a218";
// string lockUtxoTxHash = "b31ad99b554d56dc51c73dd98f947533b6ef4210314060a3ea17ad4eca5d3d38";

// var unlockLovelace = TransactionTemplateBuilder<UnlockParameters>.Create(provider)
//     .AddStaticParty("rico", ricoAddress, true)
//     .AddStaticParty("validator", validatorAddress)
//     .AddInput((options, unlockParams) =>
//     {
//         options.From = "validator";
//         options.UtxoRef = new TransactionInput(Convert.FromHexString(scriptRefTxHash), 0);
//         options.IsReference = true;
//     })
//     .AddInput((options, unlockParams) =>
//     {
//         options.From = "validator";
//         options.UtxoRef = new TransactionInput(Convert.FromHexString(lockUtxoTxHash), 0);
//         options.Redeemer = unlockParams.Redeemer;
//     })
//     .AddOutput((options, unlockParams) =>
//     {
//         options.To = "rico";
//         options.Amount = unlockParams.Amount;
//     })
//     .Build();


// // UnlockParameters unlockParams = new(
// //     new TransactionInput(Convert.FromHexString(lockUtxoTxHash), 0),
// //     new TransactionInput(Convert.FromHexString(scriptRefTxHash), 0),
// //     new Lovelace(20000000),
// //     new RedeemerMap(new Dictionary<RedeemerKey, RedeemerValue>() { { new RedeemerKey(0, 0), new RedeemerValue(new PlutusConstr([]) { ConstrIndex = 121 }, new ExUnits(1400000, 100000000)) } }
// // ));

// Transaction unlockUnsignedTx = await unlockLovelace(unlockParams);
// Transaction unlockSignedTx = unlockUnsignedTx.Sign(privateKey);

// Example using the improved redeemer handling approach
// Console.WriteLine("\nImproved redeemer approach result:");
// byte[] improvedRedeemerResult = await ImprovedRedeemerSample.UnlockLovelaceWithImprovedRedeemerAsync();
// Console.WriteLine(Convert.ToHexString(improvedRedeemerResult));
// Console.WriteLine(Convert.ToHexString(CborSerializer.Serialize(unlockSignedTx)));

