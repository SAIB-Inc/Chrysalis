using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Custom;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Network.Cbor.LocalStateQuery.Messages;

[CborConverter(typeof(CustomListConverter))]
public partial record Release(
    [CborIndex(0)][ExactValue(5)] ExactValue<CborInt> Idx
) : LocalStateQueryMessage;
