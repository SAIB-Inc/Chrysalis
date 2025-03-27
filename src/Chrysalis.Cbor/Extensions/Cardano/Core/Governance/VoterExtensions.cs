using Chrysalis.Cbor.Types.Cardano.Core.Governance;

namespace Chrysalis.Cbor.Extensions.Cardano.Core.Governance;

public static class VoterExtensions
{
    public static int Tag(this Voter self) => self.Tag;

    public static byte[] Hash(this Voter self) => self.Hash;
}