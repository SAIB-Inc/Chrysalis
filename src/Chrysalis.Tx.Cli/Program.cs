using Chrysalis.Tx;
using Chrysalis.Tx.Models;
using Chrysalis.Tx.Cli;


var alice = new Address("Alice");
var bob = new Address("Bob");

//Simple Transfer
// users can use any type as parameters
var transfer = TxTemplateBuilder<ulong>.Create()
    .AddParty("alice", alice)
    .AddParty("bob", bob)
    .AddInput((options, amount) => {
        options.From = "alice";
        options.MinAmount = new Value(AssetType.Ada, amount);
    })
    .AddOutput((options, amount) => {
        options.To = "bob";
        options.Amount = new Value(AssetType.Ada, amount);
    })
    .Build();

string transferUnSignedTx = transfer(1000);


var validator = new Address("Validator");

// Lock and Unlock
// Users can create their own custom datatypes and use them as parameters
var lockTx = TxTemplateBuilder<LockParameters>.Create()
    .AddParty("alice", alice)
    .AddParty("validator", validator)
    .AddInput((options, lockParams) => {
        options.From = "alice";
        options.MinAmount = lockParams.Amount;
    })
    .AddOutput((options, lockParams) => {
        options.To = "validator";
        options.Amount = lockParams.Amount;
        options.Datum = lockParams.Datum;
    })
    .Build();

string lockUnSignedTx = lockTx(new LockParameters(new Value(AssetType.Ada, 1000UL), new Datum("hello")));

var unlockTx = TxTemplateBuilder<UnlockParameters>.Create()
    .AddParty("alice", alice)
    .AddParty("validator", validator)
    .AddInput((options, unlockParams) => {
        options.From = "validator";
        options.UtxoRef = unlockParams.UtxoRef;
        options.Redeemer = unlockParams.Redeemer;
    })
    // Since this is unlock tx the amount is calculated automatically unless the user wants to specify
    .AddOutput((options, unlockParams) => {
        options.To = "alice";
    })
    .Build();

var unlockUnSignedTx = unlockTx(new UnlockParameters("locked_utxo", new Redeemer("hello")));
