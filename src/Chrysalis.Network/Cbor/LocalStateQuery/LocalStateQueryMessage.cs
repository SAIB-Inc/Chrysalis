using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Network.Cbor.LocalStateQuery;

[CborSerializable]
[CborUnion]
public abstract partial record LocalStateQueryMessage : CborBase;

