using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.Protocol;

// CostMdls uses CborMaybeIndefList to allow both definite and indefinite encoding
// The validator enforces the correct encoding based on the Plutus version
[CborSerializable]
public partial record CostMdls(Dictionary<int, CborMaybeIndefList<long>> Value) : CborBase;

// Validator to enforce correct encoding for each Plutus version
public class CostMdlsValidator : ICborValidator<CostMdls>
{
    public bool Validate(CostMdls input)
    {
        foreach (var kvp in input.Value)
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