using System.Numerics;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core.Common;
using Chrysalis.Plutus.Types;
using Chrysalis.Tx.Extensions;
using Xunit;

namespace Chrysalis.Tx.Test;

public class X7PlutusMapTests
{
    [Fact]
    public void CodecMapKeepsBothEntriesWhenConvertedToVmData()
    {
        // Data.hs:42–47,277–283: Plutus Data stores a map as a list of pairs.
        IPlutusData datum = IPlutusData.Read(Convert.FromHexString("A201020103"));
        PlutusDataMap vm = Assert.IsType<PlutusDataMap>(ScriptContextBuilder.CodecPlutusDataToVm(datum));
        Assert.Equal(2, vm.Entries.Length);
        Assert.Equal(BigInteger.One, Assert.IsType<PlutusDataInteger>(vm.Entries[0].Key).Value);
        Assert.Equal(BigInteger.One, Assert.IsType<PlutusDataInteger>(vm.Entries[1].Key).Value);
        Assert.Equal(new BigInteger(2), Assert.IsType<PlutusDataInteger>(vm.Entries[0].Value).Value);
        Assert.Equal(new BigInteger(3), Assert.IsType<PlutusDataInteger>(vm.Entries[1].Value).Value);
    }

    [Fact]
    public void ExtendedConstructorIndexDoesNotGetReadAsAShortFormTag()
    {
        IPlutusData datum = IPlutusData.Read(Convert.FromHexString("D8668218798101"));
        PlutusDataConstr vm = Assert.IsType<PlutusDataConstr>(ScriptContextBuilder.CodecPlutusDataToVm(datum));
        Assert.Equal(new BigInteger(121), vm.Tag);
        Assert.Equal(BigInteger.One, Assert.IsType<PlutusDataInteger>(Assert.Single(vm.Fields)).Value);
    }
    [Theory]
    [InlineData(6, "D87F80")]
    [InlineData(7, "D9050080")]
    [InlineData(127, "D9057880")]
    [InlineData(128, "D86682188080")]
    [InlineData(int.MaxValue, "D866821A7FFFFFFF80")]
    public void X7CreateChoosesCanonicalConstructorTag(int alternative, string hex)
    {
        PlutusConstr data = PlutusConstr.Create(alternative, CborDefList<IPlutusData>.Create([]));
        Assert.Equal(Convert.FromHexString(hex), data.Raw.ToArray());
        PlutusDataConstr vm = Assert.IsType<PlutusDataConstr>(ScriptContextBuilder.CodecPlutusDataToVm(IPlutusData.Read(data.Raw)));
        Assert.Equal(new BigInteger(alternative), vm.Tag);
    }
}
