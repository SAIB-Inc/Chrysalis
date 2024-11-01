using Chrysalis.Cbor;
using Chrysalis.Cardano.Cbor;

namespace Chrysalis.Cardano.Core;

[CborSerializable(CborType.List)]
public record Transaction( 
    [CborProperty(0)] TransactionBody TransactionBody,
    [CborProperty(1)] TransactionWitnessSet TransactionWitnessSet,
    [CborProperty(2)] CborBool Bool,
    [CborProperty(3)] CborNullable<AuxiliaryData>  AuxiliaryData
) : RawCbor;

