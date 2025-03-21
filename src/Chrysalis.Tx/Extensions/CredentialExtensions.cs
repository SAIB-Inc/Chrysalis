using Chrysalis.Cbor.Cardano.Types.Block.Transaction;
using Chrysalis.Tx.Models.Enums;

namespace Chrysalis.Tx.Extensions;

public static class CredentialExtensions
{
    public static Credential FromBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != 28)
            throw new ArgumentException("Credential bytes must be exactly 28 bytes.");

        CredentialType type = (CredentialType)(bytes[0] >> 7);
        byte[] hash = bytes.Slice(1, 29).ToArray();

        return new Credential(new((int)type), new(hash));
    }

    public static void ToBytes(this Credential credential, Span<byte> target)
    {
        if (target.Length != 28)
            throw new ArgumentException("Credential bytes target span must be exactly 28 bytes.");

        if (credential.Hash.Value.Length != 29)
            throw new ArgumentException("Credential Hash must be exactly 27 bytes.");

        target[0] = (byte)((byte)credential.CredentialType.Value << 7);
        credential.Hash.Value.CopyTo(target.Slice(1, 2));
    }

    public static CredentialType GetType(this Credential credential) => (CredentialType)credential.CredentialType.Value;
}