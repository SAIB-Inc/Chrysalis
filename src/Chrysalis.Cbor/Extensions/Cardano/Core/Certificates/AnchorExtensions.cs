using Chrysalis.Cbor.Types.Cardano.Core.Transaction;

namespace Chrysalis.Cbor.Extensions.Cardano.Core.Certificates;

public static class AnchorExtensions
{
    public static string Url(this Anchor self) => self.AnchorUrl;

    public static byte[] DataHash(this Anchor self) => self.AnchorDataHash;
}