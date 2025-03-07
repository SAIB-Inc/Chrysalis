using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Network.Cbor.LocalStateQuery;

[CborConverter(typeof(UnionConverter))]
public abstract record LocalStateQueryMessage : CborBase;

