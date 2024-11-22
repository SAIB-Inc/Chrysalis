using Chrysalis.Cardano.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Core;

public record AuxiliaryDataSet(Dictionary<CborInt, AuxiliaryData> Value)
    : CborMap<CborInt, AuxiliaryData>(Value), ICbor;