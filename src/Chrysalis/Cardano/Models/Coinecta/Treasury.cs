using Chrysalis.Cbor;
using Chrysalis.Cardano.Models.Sundae;
using Chrysalis.Cardano.Models.Cbor;

namespace Chrysalis.Cardano.Models.Coinecta;

[CborSerializable(CborType.Constr, Index = 0)]
public record Treasury(
    MultisigScript Owner,
    CborBytes TreasuryRootHash,
    PosixTime UnlockTime
) : ICbor;


