using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cardano.Core.Types.Block.Transaction.Governance;

[CborConverter(typeof(MapConverter))]
public record MemberTermLimits(Dictionary<Credential, CborUlong> Value) : CborBase;