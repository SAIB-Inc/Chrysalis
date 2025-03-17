namespace Chrysalis.Tx.Models;


public record InputOptions(string From, Value? MinAmount, IData? Datum, IData? Redeemer, string? UtxoRef)
{
    public string From { get; set; } = From;
    public Value? MinAmount { get; set; } = MinAmount;
    public string? UtxoRef { get; set; } = UtxoRef;
    public IData? Datum { get; set; } = Datum;
    public IData? Redeemer { get; set; } = Redeemer;
}

public class OutputOptions(string To, Value? Amount, IData? Datum)
{
    public string To { get; set; } = To;
    public Value? Amount { get; set; } = Amount;
    public IData? Datum { get; set; } = Datum;

}