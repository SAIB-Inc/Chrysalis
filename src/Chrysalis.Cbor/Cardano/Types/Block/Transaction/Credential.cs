using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction;

[CborConverter(typeof(CustomListConverter))]
public partial record Credential(
    [CborIndex(0)] CborInt CredentialType,
    [CborIndex(1)] CborBytes Hash
) : CborBase;