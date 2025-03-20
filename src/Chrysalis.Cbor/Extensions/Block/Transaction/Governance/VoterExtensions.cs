using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Governance;

namespace Chrysalis.Cbor.Extensions.Block.Transaction.Governance;

public static class VoterExtensions
{
    public static int Tag(this Voter self) => self.Tag;

    public static byte[] Hash(this Voter self) => self.Hash;
}