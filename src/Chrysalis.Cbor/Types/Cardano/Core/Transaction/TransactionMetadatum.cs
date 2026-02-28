using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.Transaction;

/// <summary>
/// Abstract base for transaction metadata values, supporting maps, lists, bytes, text, and integers.
/// </summary>
[CborSerializable]
[CborUnion]
public abstract partial record TransactionMetadatum : CborBase
{
}

/// <summary>
/// A metadata value containing a map of metadatum key-value pairs.
/// </summary>
/// <param name="Value">The dictionary mapping metadatum keys to metadatum values.</param>
[CborSerializable]
public partial record MetadatumMap(
    Dictionary<TransactionMetadatum, TransactionMetadatum> Value
) : TransactionMetadatum;

/// <summary>
/// A metadata value containing an ordered list of metadatum values.
/// </summary>
/// <param name="Value">The list of metadatum values.</param>
[CborSerializable]
public partial record MetadatumList(
    List<TransactionMetadatum> Value
) : TransactionMetadatum;

/// <summary>
/// A metadata value containing raw bytes.
/// </summary>
/// <param name="Value">The byte array metadata value.</param>
[CborSerializable]
public partial record MetadatumBytes(byte[] Value) : TransactionMetadatum;

/// <summary>
/// A metadata value containing a text string.
/// </summary>
/// <param name="Value">The text string metadata value.</param>
[CborSerializable]
public partial record MetadataText(string Value) : TransactionMetadatum;

/// <summary>
/// Abstract base for integer metadata values, which can be signed (long) or unsigned (ulong).
/// </summary>
[CborSerializable]
[CborUnion]
public abstract partial record MetadatumInt : TransactionMetadatum
{
}

/// <summary>
/// A metadata integer value stored as a signed long.
/// </summary>
/// <param name="Value">The signed long integer metadata value.</param>
[CborSerializable]
public partial record MetadatumIntLong(long Value) : MetadatumInt;

/// <summary>
/// A metadata integer value stored as an unsigned long.
/// </summary>
/// <param name="Value">The unsigned long integer metadata value.</param>
[CborSerializable]
public partial record MetadatumIntUlong(ulong Value) : MetadatumInt;
