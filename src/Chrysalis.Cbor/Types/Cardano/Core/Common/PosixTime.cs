using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Types.Cardano.Core.Common;

[CborSerializable]
public partial record PosixTime(ulong Value) : CborBase;
