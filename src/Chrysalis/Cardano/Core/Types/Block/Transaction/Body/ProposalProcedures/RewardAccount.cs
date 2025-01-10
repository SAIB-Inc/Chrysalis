using Chrysalis.Cbor.Abstractions;
using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cardano.Core.Types.Block.Transaction.Body.ProposalProcedures;

[CborConverter(typeof(BytesConverter))]
public record RewardAccount(ReadOnlyMemory<byte> Value) : CborBase;