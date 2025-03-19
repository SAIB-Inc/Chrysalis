using Chrysalis.Cbor.Types;
using Chrysalis.Tx.Models;

namespace Chrysalis.Tx.Cli;

public record Datum(string Value) : CborBase{
    public string Value { get; init; } = Value;
}

