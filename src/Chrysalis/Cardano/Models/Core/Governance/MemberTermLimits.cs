using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Core.Governance;

public record MemberTermLimits(Dictionary<Credential, CborUlong> Value)
    : CborMap<Credential, CborUlong>(Value), ICbor;