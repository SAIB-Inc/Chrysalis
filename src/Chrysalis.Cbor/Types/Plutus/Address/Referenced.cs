using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Plutus.Address;

/// <summary>
/// Abstract base for referenced values that can be either inline or referenced by pointer.
/// </summary>
/// <typeparam name="T">The type of the referenced value.</typeparam>
[CborSerializable]
[CborUnion]
public abstract partial record Referenced<T> : CborBase;

/// <summary>
/// An inline referenced value directly containing its data.
/// </summary>
/// <typeparam name="T">The type of the inline value.</typeparam>
/// <param name="Value">The inline value.</param>
[CborSerializable]
[CborConstr(0)]
public partial record Inline<T>([CborOrder(0)] T Value) : Referenced<T>;

/// <summary>
/// A pointer-based reference to a value using chain position coordinates.
/// </summary>
/// <typeparam name="T">The type of the pointed-to value.</typeparam>
/// <param name="SlotNumber">The slot number containing the referenced registration.</param>
/// <param name="TransactionIndex">The transaction index within the slot.</param>
/// <param name="CertificateIndex">The certificate index within the transaction.</param>
[CborSerializable]
[CborConstr(1)]
public partial record PointerRef<T>(
    [CborOrder(0)] ulong SlotNumber,
    [CborOrder(1)] ulong TransactionIndex,
    [CborOrder(2)] ulong CertificateIndex
) : Referenced<T>;
