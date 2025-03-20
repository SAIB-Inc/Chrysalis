using Chrysalis.Cbor.Cardano.Types.Block.Header;
using Chrysalis.Cbor.Cardano.Types.Block.Header.Body;

namespace Chrysalis.Cbor.Extensions.Block.Header;

public static class HeaderExtensions
{
    public static BlockHeaderBody HeaderBody(this BlockHeader self) => self.HeaderBody;

    public static byte[] BodySignature(this BlockHeader self) => self.BodySignature;
}