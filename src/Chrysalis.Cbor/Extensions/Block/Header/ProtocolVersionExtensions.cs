using Chrysalis.Cbor.Cardano.Types.Block.Header.Body;

namespace Chrysalis.Cbor.Extensions.Block.Header;

public static class ProtocolVersionExtensions
{
    public static int MajorProtocolVersion(this ProtocolVersion self) => self.MajorProtocolVersion;

    public static ulong SequenceNumber(this ProtocolVersion self) => self.SequenceNumber;
}