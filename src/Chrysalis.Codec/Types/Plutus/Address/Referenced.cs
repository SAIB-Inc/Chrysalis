using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Attributes;

namespace Chrysalis.Codec.Types.Plutus.Address;

/// <summary>
/// Abstract base for referenced values that can be either inline or referenced by pointer.
/// </summary>
/// <typeparam name="T">The type of the referenced value.</typeparam>
[CborSerializable]
[CborUnion]
public partial interface IReferenced<T> : ICborType;

/// <summary>
/// An inline referenced value directly containing its data.
/// </summary>
/// <typeparam name="T">The type of the inline value.</typeparam>
[CborSerializable]
[CborConstr(0)]
public readonly partial record struct Inline<T> : IReferenced<T>
{
    [CborOrder(0)] public partial T Value { get; }
}

/// <summary>
/// A pointer-based reference to a value using chain position coordinates.
/// </summary>
/// <typeparam name="T">The type of the pointed-to value.</typeparam>
[CborSerializable]
[CborConstr(1)]
public readonly partial record struct PointerRef<T> : IReferenced<T>
{
    [CborOrder(0)] public partial ulong SlotNumber { get; }
    [CborOrder(1)] public partial ulong TransactionIndex { get; }
    [CborOrder(2)] public partial ulong CertificateIndex { get; }
}
