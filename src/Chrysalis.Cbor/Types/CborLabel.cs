using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types;

/// <summary>
/// COSE Label type that can be either an integer or a text string.
/// Based on RFC 8152: label = int / tstr
/// </summary>
[CborSerializable]
public partial record CborLabel(object Value) : CborBase;