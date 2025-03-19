using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Output;
using Chrysalis.Tx.Models;

namespace Chrysalis.Tx.Cli;


public record LockParameters(Value Amount, Datum Datum){
    public Value Amount { get; init; } = Amount;
    public Datum Datum { get; init; } = Datum;
}

public record UnlockParameters(string UtxoRef, Redeemer Redeemer){
    public string UtxoRef { get; init; } = UtxoRef;
    public Redeemer Redeemer { get; init; } = Redeemer;
}

public record TransferParameters(ulong Amount, Dictionary<string, string> Parties) : IParameters{
    public ulong Amount { get; init; } = Amount;

    public Dictionary<string, string> Parties { get; set; } = Parties;
}