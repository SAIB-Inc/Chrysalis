using Chrysalis.Codec.Types;
using SAIB.Cbor.Serialization;

namespace Chrysalis.Network.Cbor.Handshake;

// The general generated list reader does not check exhaustion. Handshake replies
// are one-shot: validate their original array headers before interpreting them.
internal static class HandshakeWireValidation
{
    internal static void Validate(HandshakeMessage message)
    {
        RequireArray(message, message is N2CAcceptVersion or N2NAcceptVersion ? 3 : 2);
        switch (message)
        {
            case N2CAcceptVersion accept:
                RequireArray(accept.VersionData, 2);
                break;
            case N2NAcceptVersion accept:
                ValidateVersionData(accept.VersionData);
                break;
            case Refuse refuse:
                RequireArray(refuse.Reason, refuse.Reason is VersionMismatch ? 2 : 3);
                break;
            case ProposeVersions propose:
                ValidateTable(propose.VersionTable);
                break;
            case N2CQueryReply reply:
                ValidateTable(reply.VersionTable);
                break;
            case N2NQueryReply reply:
                ValidateTable(reply.VersionTable);
                break;
            default:
                throw new FormatException("Unknown handshake message shape.");
        }
    }

    private static void ValidateTable(VersionTable table)
    {
        if (!HandshakeVersionTables.ValidateWire(table))
        {
            throw new FormatException("Handshake version tables require definite, strictly ascending, distinct keys.");
        }
        switch (table)
        {
            case N2CVersionTable n2c:
                foreach (KeyValuePair<N2CVersion, N2CVersionData> entry in n2c.Value)
                {
                    RequireArray(entry.Value, 2);
                }
                break;
            case N2NVersionTable n2n:
                foreach (KeyValuePair<N2NVersion, N2NVersionData> entry in n2n.Value)
                {
                    ValidateVersionData(entry.Value);
                }
                break;
            default:
                throw new FormatException("Unknown handshake version-table family.");
        }
    }

    private static void ValidateVersionData(N2NVersionData data) =>
        RequireArray(data, 2 + (data.PeerSharing.HasValue ? 1 : 0) + (data.Query.HasValue ? 1 : 0));

    private static void RequireArray(CborRecord record, int arity)
    {
        CborReader reader = new(record.Raw.Span);
        reader.ReadBeginArray();
        if (reader.ReadSize() != arity)
        {
            throw new FormatException("Malformed fixed-arity handshake array.");
        }
    }
}
