using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Core.Transaction;

public record AuxiliaryDataSet(Dictionary<CborInt, AuxiliaryData> Value)
    : CborMap<CborInt, AuxiliaryData>(Value), ICbor;