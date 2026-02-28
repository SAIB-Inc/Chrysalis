using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.Byron;

/// <summary>
/// Byron transaction witness encoded as [variant_byte, #6.24(cbor(data))].
/// Variant 0: PkWitness [pubkey, signature]
/// Variant 1: ScriptWitness [validator_script, redeemer_script]
/// Variant 2: RedeemWitness [pubkey, signature]
/// </summary>
/// <param name="Variant">The witness variant type.</param>
/// <param name="Data">The encoded witness data.</param>
[CborSerializable]
[CborList]
public partial record ByronTxWitness(
    [CborOrder(0)] int Variant,
    [CborOrder(1)] CborEncodedValue Data
) : CborBase;
