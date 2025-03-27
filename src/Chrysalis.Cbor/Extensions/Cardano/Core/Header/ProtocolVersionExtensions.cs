using Chrysalis.Cbor.Types.Cardano.Core.Header;

namespace Chrysalis.Cbor.Extensions.Cardano.Core.Header;

public static class ProtocolVersionExtensions
{
    public static int MajorProtocolVersion(this ProtocolVersion self) => self.MajorProtocolVersion;

    public static ulong SequenceNumber(this ProtocolVersion self) => self.SequenceNumber;
}