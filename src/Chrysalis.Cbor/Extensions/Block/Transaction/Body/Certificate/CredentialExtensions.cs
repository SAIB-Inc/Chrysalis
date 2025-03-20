using Chrysalis.Cbor.Cardano.Types.Block.Transaction;

namespace Chrysalis.Cbor.Extensions.Block.Transaction.Body.Certificate;

public static class CredentialExtensions
{
    public static int Type(this Credential self) => self.CredentialType;

    public static byte[] Hash(this Credential self) => self.Hash;
}