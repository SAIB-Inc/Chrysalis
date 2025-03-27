using Chrysalis.Cbor.Types.Cardano.Core.Header;

namespace Chrysalis.Cbor.Extensions.Cardano.Core.Header;

public static class HeaderExtensions
{
    public static BlockHeaderBody HeaderBody(this BlockHeader self) => self.HeaderBody;

    public static byte[] BodySignature(this BlockHeader self) => self.BodySignature;
}