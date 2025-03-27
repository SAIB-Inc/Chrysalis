using Chrysalis.Cbor.Types.Cardano.Core.Transaction;

namespace Chrysalis.Cbor.Extensions.Cardano.Core.Certificates;

public static class CredentialExtensions
{
    public static int Type(this Credential self) => self.CredentialType;

    public static byte[] Hash(this Credential self) => self.Hash;
}