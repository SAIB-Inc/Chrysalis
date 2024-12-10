using Chrysalis.Cardano.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Core;

public record MemberTermLimits(Dictionary<Credential, CborUlong> Value)
    : CborMap<Credential, CborUlong>(Value), ICbor;