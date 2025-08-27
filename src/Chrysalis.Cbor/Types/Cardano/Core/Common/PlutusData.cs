using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Types.Cardano.Core.Common;

[CborSerializable]
[CborUnion]
public abstract partial record PlutusData : CborBase, ICborPreserveRaw;

// PlutusConstr with index for validation
[CborSerializable]
[CborConstr]
public partial record PlutusConstr(CborMaybeIndefList<PlutusData> PlutusData) : PlutusData
{
    // Store the constructor index if known (set during deserialization)
    public int? ConstructorIndex { get; init; }
}

// Validator to enforce correct PlutusConstr encoding based on constructor tag
public class PlutusConstrValidator : ICborValidator<PlutusConstr>
{
    public bool Validate(PlutusConstr input)
    {
        // If we know the constructor index, validate the encoding
        if (input.ConstructorIndex.HasValue)
        {
            var index = input.ConstructorIndex.Value;
            
            // Constructor alternatives 0-6 (tags 121-127) must use indefinite encoding
            if (index >= 0 && index <= 6)
            {
                if (input.PlutusData is not CborIndefList<PlutusData>)
                {
                    throw new InvalidOperationException(
                        $"PlutusConstr alternative {index} (tag {121 + index}) must use indefinite array encoding");
                }
            }
            // General constructor (tag 102) - the inner list should be indefinite
            // but the outer structure is [index, [* data]] which is definite
            else
            {
                // For tag 102, we typically use CborIndefList for the data
                // The wrapper array [index, data] is handled at serialization level
                if (input.PlutusData is not CborIndefList<PlutusData>)
                {
                    throw new InvalidOperationException(
                        $"PlutusConstr general constructor (tag 102) data must use indefinite array encoding");
                }
            }
        }
        
        return true;
    }
}

[CborSerializable]
public partial record PlutusMap(Dictionary<PlutusData, PlutusData> PlutusData) : PlutusData;

[CborSerializable]
public partial record PlutusList(CborMaybeIndefList<PlutusData> PlutusData) : PlutusData;


[CborSerializable]
[CborUnion]
public abstract partial record PlutusBigInt : PlutusData
{
}

[CborSerializable]
[CborUnion]
public abstract partial record PlutusInt : PlutusBigInt
{
}

[CborSerializable]
public partial record PlutusInt64(long Value) : PlutusInt;

[CborSerializable]
public partial record PlutusUint64(ulong Value) : PlutusInt;

[CborSerializable]
[CborTag(2)]
public partial record PlutusBigUint([CborSize(64)] byte[] Value) : PlutusBigInt;

[CborSerializable]
[CborTag(3)]
public partial record PlutusBigNint([CborSize(64)] byte[] Value) : PlutusBigInt;

[CborSerializable]
public partial record PlutusBoundedBytes([CborSize(64)] byte[] Value) : PlutusData;