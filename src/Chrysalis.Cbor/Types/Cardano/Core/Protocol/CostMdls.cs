using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.Protocol;

/// <summary>
/// Cost models for Plutus script languages, mapping language version keys to their cost parameter lists.
/// Uses CborMaybeIndefList to allow both definite and indefinite encoding per Plutus version.
/// </summary>
/// <param name="Value">Dictionary mapping Plutus language version (0=V1, 1=V2, 2=V3) to cost parameters.</param>
[CborSerializable]
public partial record CostMdls(Dictionary<int, CborMaybeIndefList<long>> Value) : CborBase;

/// <summary>
/// Validator ensuring correct encoding format for each Plutus language version cost model.
/// PlutusV1 must use indefinite encoding; PlutusV2 and PlutusV3 must use definite encoding.
/// </summary>
public class CostMdlsValidator : ICborValidator<CostMdls>
{
    /// <summary>
    /// Validates that each cost model entry uses the correct encoding for its Plutus version.
    /// </summary>
    /// <param name="input">The cost models to validate.</param>
    /// <returns>True if all cost model encodings are valid.</returns>
    /// <exception cref="InvalidOperationException">Thrown when a cost model uses incorrect encoding.</exception>
    public bool Validate(CostMdls input)
    {
        ArgumentNullException.ThrowIfNull(input);

        foreach (KeyValuePair<int, CborMaybeIndefList<long>> kvp in input.Value)
        {
            switch (kvp.Key)
            {
                case 0:
                    // PlutusV1 must use indefinite encoding
                    if (kvp.Value is not CborIndefList<long>)
                    {
                        throw new InvalidOperationException(
                            "PlutusV1 cost model (key 0) must use indefinite array encoding (CborIndefList)");
                    }
                    break;

                case 1:
                case 2:
                    // PlutusV2 and PlutusV3 must use definite encoding
                    if (kvp.Value is not CborDefList<long>)
                    {
                        throw new InvalidOperationException(
                            $"Plutus{(kvp.Key == 1 ? "V2" : "V3")} cost model (key {kvp.Key}) must use definite array encoding (CborDefList)");
                    }
                    break;

                default:
                    // Other versions (3..255) can use either encoding
                    break;
            }
        }

        return true;
    }
}
