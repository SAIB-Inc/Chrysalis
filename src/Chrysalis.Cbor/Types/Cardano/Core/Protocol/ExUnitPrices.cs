using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.Protocol;

/// <summary>
/// Execution unit prices for Plutus script evaluation, specifying memory and step costs.
/// </summary>
/// <param name="MemPrice">The price per memory unit as a rational number.</param>
/// <param name="StepPrice">The price per CPU step as a rational number.</param>
[CborSerializable]
[CborList]
public partial record ExUnitPrices(
    [CborOrder(0)] CborRationalNumber MemPrice,
    [CborOrder(1)] CborRationalNumber StepPrice
) : CborBase;
