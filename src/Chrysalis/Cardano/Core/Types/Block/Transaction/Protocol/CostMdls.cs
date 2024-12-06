using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Collections;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cardano.Core.Types.Block.Transaction.Protocol;

[CborConverter(typeof(MapConverter))]
public record CostMdls(Dictionary<CborInt, CborDefiniteList<CborUlong>> Value) : CborBase;