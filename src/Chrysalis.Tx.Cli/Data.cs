using Chrysalis.Tx.Models;

namespace Chrysalis.Tx.Cli;

public record Datum(string Value) : IData{
    public string Value { get; init; } = Value;
}

public record Redeemer(string Value) : IData{
    public string Value { get; init; } = Value;
}