using Chrysalis.Cardano.Crashr.Types.Datums;
using Chrysalis.Cbor.Converters;
using Xunit;

namespace Chrysalis.Test;

public class CborDeserializerTests
{
    [Theory]
    [InlineData("as")]
    public void DeserializeListingDatumTest(string hex)
    {
        byte[] data = Convert.FromHexString("d8799f9fd8799fd8799fd8799f581c7060251a30c40e428085cdb477aa0ca8462ae5d8b6a55e5e9616aeb6ffd8799fd8799fd8799f581c850282937506f53d58e9e6fd7ccbbbc57196d58b2a11e36a76a60857ffffffffa140a1401a02dc6c00ffff581c7060251a30c40e428085cdb477aa0ca8462ae5d8b6a55e5e9616aeb6ff");
        ListingDatum datum = CborSerializer.Deserialize<ListingDatum>(data);

        Assert.Equal(true, true);
    }
}
