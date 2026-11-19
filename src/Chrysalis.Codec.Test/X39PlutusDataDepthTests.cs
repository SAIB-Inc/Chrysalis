using System.Runtime.ExceptionServices;
using Chrysalis.Codec.Extensions.Cardano.Core.TransactionWitness;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core.Common;
using Chrysalis.Codec.Types.Cardano.Core.TransactionWitness;

namespace Chrysalis.Codec.Test;

public class X39PlutusDataDepthTests
{
    // Mainnet maxTxSize is 16,384 bytes (Shelley genesis); the smallest PlutusData level is 1 byte (0x81)
    // and the smallest leaf is 1 byte (0x00), so 16,383 levels is the deepest PlutusData a transaction can carry.
    internal const int Depth = 16_383;

    // Every shape checks read, write, size, compare, ToString and one lazy child step at Depth;
    // only the map row walks all 16,383 levels because the lazy walk is O(Depth^2).
    [Theory]
    [InlineData("81", "", false)]
    [InlineData("9f", "ff", false)]
    [InlineData("d87981", "", false)]
    [InlineData("a100", "", true)]
    public void X39DeepDataDecodesWritesSizesComparesAndWalks(string open, string close, bool walk)
    {
        byte[] bytes = Nest(open, close, "00", Depth);

        OnDefaultStack(() =>
        {
            IPlutusData root = CborSerializer.Deserialize<IPlutusData>(bytes);
            Assert.Equal(bytes, CborSerializer.Serialize(root));
            Assert.Equal(bytes.Length, CborSerializer.GetEncodedLength(root));

            IPlutusData again = CborSerializer.Deserialize<IPlutusData>(bytes);
            Assert.True(root.Equals(again));
            Assert.Equal(root.GetHashCode(), again.GetHashCode());
            _ = root.Equals(CborSerializer.Deserialize<IPlutusData>(bytes.ToArray()));

            Assert.False(string.IsNullOrEmpty(root.ToString()));

            if (!walk)
            {
                Assert.NotNull(Child(root));
                return;
            }

            IPlutusData current = root;
            int levels = 0;
            while (Child(current) is IPlutusData next)
            {
                current = next;
                levels++;
            }

            Assert.Equal(Depth, levels);
            PlutusUint64 leaf = Assert.IsType<PlutusUint64>(current);
            Assert.Equal(0UL, leaf.Value);
        });
    }

    [Fact]
    public void X39DeepBuiltListWritesTheSameBytes()
    {
        byte[] expected = Nest("81", "", "00", Depth);

        OnDefaultStack(() =>
        {
            IPlutusData built = PlutusUint64.Create(0);
            for (int i = 0; i < Depth; i++)
            {
                built = PlutusList.Create(CborList.Create([built]));
            }

            Assert.Equal(expected, CborSerializer.Serialize(built));
            Assert.Equal(expected.Length, CborSerializer.GetEncodedLength(built));
        });
    }

    [Fact]
    public void X39DeepDatumInAWitnessSetDecodes()
    {
        byte[] datum = Nest("81", "", "00", Depth);
        byte[] witnessSet = [0xA1, 0x04, 0x81, .. datum];

        OnDefaultStack(() =>
        {
            ITransactionWitnessSet set = CborSerializer.Deserialize<ITransactionWitnessSet>(witnessSet);
            IEnumerable<IPlutusData>? dataSet = set.PlutusDataSet();
            Assert.NotNull(dataSet);
            IPlutusData only = Assert.Single(dataSet);
            Assert.True(only.Raw.Span.SequenceEqual(datum));
        });
    }

    private static byte[] Nest(string open, string close, string leaf, int depth) =>
        [
            .. Enumerable.Repeat(Convert.FromHexString(open), depth).SelectMany(level => level),
            .. Convert.FromHexString(leaf),
            .. Enumerable.Repeat(Convert.FromHexString(close), depth).SelectMany(level => level),
        ];

    private static IPlutusData? Child(IPlutusData node) =>
        node switch
        {
            PlutusList list => Only(list.Value),
            PlutusConstr constr => Only(constr.Fields),
            PlutusMap map => map.Value[0].Value,
            _ => null,
        };

    private static IPlutusData Only(CborList<IPlutusData> items)
    {
        IPlutusData? found = null;
        foreach (IPlutusData item in items)
        {
            Assert.Null(found);
            found = item;
        }

        Assert.NotNull(found);
        return found;
    }

    // Runs on a fresh Thread of the runtime's default stack size so the test runner thread cannot mask overflow.
    // Kept local because Chrysalis.Codec.Test cannot reference Chrysalis.Tx.Test.
    private static void OnDefaultStack(Action run)
    {
        ExceptionDispatchInfo? error = null;
        Thread thread = new(() =>
        {
            try
            {
                run();
            }
            catch (Exception ex)
            {
                error = ExceptionDispatchInfo.Capture(ex);
            }
        });
        thread.Start();
        thread.Join();
        error?.Throw();
    }
}
