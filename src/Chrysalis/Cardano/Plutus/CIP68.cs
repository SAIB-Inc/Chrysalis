using Chrysalis.Cardano.Cbor;
using Chrysalis.Cardano.Core;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Plutus;

[CborSerializable(CborType.Constr, Index = 0)]
public record CIP68<T>() : RawCbor where T : ICbor
{
    [CborProperty(0)]
    public required CborMap Metadata { get; init; }

    [CborProperty(1)]
    public required CborInt Version { get; init; }

    [CborProperty(2)]
    public required T Extra { get; init; }
}