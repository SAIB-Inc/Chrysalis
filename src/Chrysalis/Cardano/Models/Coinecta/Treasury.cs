using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Coinecta;

[CborSerializable(CborType.Constr, Index = 0)]
public record Treasury(
    MultisigScript Owner,
    CborBytes TreasuryRootHash,
    PosixTime UnlockTime
) : ICbor;