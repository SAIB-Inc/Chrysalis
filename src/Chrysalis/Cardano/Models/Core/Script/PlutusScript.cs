using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Core.Script;

[CborSerializable(CborType.Bytes)]
public record PlutusV1Script(CborBytes Value): ICbor;

[CborSerializable(CborType.Bytes)]
public record PlutusV2Script(CborBytes Value): ICbor;

[CborSerializable(CborType.Bytes)]
public record PlutusV3Script(CborBytes Value): ICbor;