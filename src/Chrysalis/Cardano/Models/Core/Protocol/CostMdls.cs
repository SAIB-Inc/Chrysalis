using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cardano.Models.Plutus;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Core.Protocol;

[CborSerializable(CborType.Map)]
public record CostMdls(
    [CborProperty(0)] Option<CborDefiniteList<CborInt>> PlutusV1,
    [CborProperty(1)] Option<CborDefiniteList<CborInt>> PlutusV2,
    [CborProperty(2)] Option<CborDefiniteList<CborInt>> PlutusV3
) : ICbor;