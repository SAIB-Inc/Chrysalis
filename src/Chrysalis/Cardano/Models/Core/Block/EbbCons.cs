using Chrysalis.Cbor;
using Chrysalis.Cardano.Models.Cbor;

namespace Chrysalis.Cardano.Models.Core.Block;

[CborSerializable(CborType.List)]
public record EbbCons(
    [CborProperty(0)] CborUlong EpochId,
    [CborProperty(1)] CborDefiniteList<CborUlong> Difficulty
) : ICbor;