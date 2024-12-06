using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cardano.Core.Types.Block.Transaction;

[CborConverter(typeof(CustomListConverter))]
public record Credential(
    [CborProperty(0)] CborInt CredentialType,
    [CborProperty(1)] CborBytes Hash
) : CborBase;