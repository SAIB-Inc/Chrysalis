using Chrysalis.Tx.TemplateBuilder;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Output;
using Chrysalis.Tx.Provider;
using Chrysalis.Tx.Cli;



var rico = "005c5c318d01f729e205c95eb1b02d623dd10e78ea58f72d0c13f892b2e8904edc699e2f0ce7b72be7cec991df651a222e2ae9244eb5975cba";
// var bob = "005c5c318d01f729e205c95eb1b02d623dd10e78ea58f72d0c13f892b2e8904edc699e2f0ce7b72be7cec991df651a222e2ae9244eb5975cba";

var provider = new Blockfrost("previewajMhMPYerz9Pd3GsqjayLwP5mgnNnZCC");

//Simple Transfer
// users can use any type as parameters
var transfer = TxTemplateBuilder<ulong>.Create(provider)
    .AddStaticParty("rico", rico)
    .AddInput((options, amount) =>
    {
        options.From = "rico";
        options.MinAmount = new Lovelace(amount);
    })
    .AddOutput((options, amount) =>
    {
        options.To = "rico";
        options.Amount = new Lovelace(amount);
    })
    .AddOutput((options, amount) =>
    {
        options.To = "rico";
        options.Amount = new Lovelace( 2648394619UL - amount - 2000000UL);
    })
    .Build();

var unsignedTx = await transfer(10000000UL);
Console.WriteLine(Convert.ToHexString(unsignedTx));

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

