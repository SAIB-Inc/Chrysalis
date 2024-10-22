using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cardano.Models.Core.Transaction;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Core.Block;

public record AuxiliaryDataSet(Dictionary<CborInt, AuxiliaryData> Value)
    : CborMap<CborInt, AuxiliaryData>(Value), ICbor;