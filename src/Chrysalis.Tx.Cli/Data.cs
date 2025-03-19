using Chrysalis.Tx.Models;

namespace Chrysalis.Tx.Cli;

public record Datum(string Value) : ICbor{
    public string Value { get; init; } = Value;
}

public record Redeemer(string Value) : ICbor{
    public string Value { get; init; } = Value;
}