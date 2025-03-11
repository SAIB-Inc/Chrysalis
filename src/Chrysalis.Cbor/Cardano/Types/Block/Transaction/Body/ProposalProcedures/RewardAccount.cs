using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Primitives;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Body.ProposalProcedures;

[CborConverter(typeof(BytesConverter))]
public partial record RewardAccount(byte[] Value) : CborBase;