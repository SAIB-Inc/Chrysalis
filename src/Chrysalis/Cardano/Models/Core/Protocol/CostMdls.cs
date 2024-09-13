using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Core.Protocol;

[CborSerializable(CborType.Map)]
public record CostMdls(
    [CborProperty(0)] CborDefiniteList<CborInt>? PlutusV1,
    [CborProperty(1)] CborDefiniteList<CborInt>? PlutusV2,
    [CborProperty(2)] CborDefiniteList<CborInt>? PlutusV3
) : ICbor;