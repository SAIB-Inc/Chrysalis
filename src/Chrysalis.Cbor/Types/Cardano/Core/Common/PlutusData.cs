using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.Common;

/// <summary>
/// Represents Plutus data used in Cardano smart contract interactions.
/// </summary>
[CborSerializable]
[CborUnion]
public abstract partial record PlutusData : CborBase, ICborPreserveRaw;

/// <summary>
/// Represents a Plutus constructor application with an index and a list of data fields.
/// </summary>
/// <param name="PlutusData">The list of constructor field values.</param>
[CborSerializable]
[CborConstr]
public partial record PlutusConstr(CborMaybeIndefList<PlutusData> PlutusData) : PlutusData
{
    /// <summary>
    /// Gets the constructor index if known (set during deserialization).
    /// </summary>
    public int? ConstructorIndex { get; init; }
}

/// <summary>
/// Validates that a PlutusConstr uses the correct encoding based on its constructor tag.
/// </summary>
public sealed class PlutusConstrValidator : ICborValidator<PlutusConstr>
{
    /// <summary>
    /// Validates that the PlutusConstr uses the correct array encoding for its constructor index.
    /// </summary>
    /// <param name="input">The PlutusConstr to validate.</param>
    /// <returns>True if validation passes.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the encoding does not match the expected format for the constructor index.</exception>
    public bool Validate(PlutusConstr input)
    {
        ArgumentNullException.ThrowIfNull(input);

        // If we know the constructor index, validate the encoding
        if (input.ConstructorIndex.HasValue)
        {
            int index = input.ConstructorIndex.Value;

            // Constructor alternatives 0-6 (tags 121-127) must use indefinite encoding
            if (index is >= 0 and <= 6)
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

/// <summary>
/// Represents a Plutus data map with key-value pairs of Plutus data.
/// </summary>
/// <param name="PlutusData">The dictionary mapping Plutus data keys to values.</param>
[CborSerializable]
public partial record PlutusMap(Dictionary<PlutusData, PlutusData> PlutusData) : PlutusData;

/// <summary>
/// Represents a Plutus data list containing ordered Plutus data elements.
/// </summary>
/// <param name="PlutusData">The list of Plutus data elements.</param>
[CborSerializable]
public partial record PlutusList(CborMaybeIndefList<PlutusData> PlutusData) : PlutusData;

/// <summary>
/// Represents a Plutus big integer value, either as a standard integer or a tagged big number.
/// </summary>
[CborSerializable]
[CborUnion]
public abstract partial record PlutusBigInt : PlutusData
{
}

/// <summary>
/// Represents a Plutus integer value that fits within standard integer ranges.
/// </summary>
[CborSerializable]
[CborUnion]
public abstract partial record PlutusInt : PlutusBigInt
{
}

/// <summary>
/// Represents a signed 64-bit Plutus integer value.
/// </summary>
/// <param name="Value">The signed 64-bit integer value.</param>
[CborSerializable]
public partial record PlutusInt64(long Value) : PlutusInt;

/// <summary>
/// Represents an unsigned 64-bit Plutus integer value.
/// </summary>
/// <param name="Value">The unsigned 64-bit integer value.</param>
[CborSerializable]
public partial record PlutusUint64(ulong Value) : PlutusInt;

/// <summary>
/// Represents a Plutus big unsigned integer encoded with CBOR tag 2.
/// </summary>
/// <param name="Value">The big unsigned integer as bounded bytes.</param>
[CborSerializable]
[CborTag(2)]
public partial record PlutusBigUint([CborSize(64)] byte[] Value) : PlutusBigInt;

/// <summary>
/// Represents a Plutus big negative integer encoded with CBOR tag 3.
/// </summary>
/// <param name="Value">The big negative integer as bounded bytes.</param>
[CborSerializable]
[CborTag(3)]
public partial record PlutusBigNint([CborSize(64)] byte[] Value) : PlutusBigInt;

/// <summary>
/// Represents Plutus bounded bytes data with a maximum size of 64 bytes.
/// </summary>
/// <param name="Value">The bounded byte array value.</param>
[CborSerializable]
public partial record PlutusBoundedBytes([CborSize(64)] byte[] Value) : PlutusData;
