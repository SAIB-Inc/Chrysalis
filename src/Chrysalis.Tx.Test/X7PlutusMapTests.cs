using System.Numerics;
using System.Runtime.ExceptionServices;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core.Common;
using Chrysalis.Codec.Types.Cardano.Core.Scripts;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Plutus.Cbor;
using Chrysalis.Plutus.Types;
using Chrysalis.Plutus.VM.Models;
using Chrysalis.Tx.Builders;
using Chrysalis.Tx.Extensions;
using Chrysalis.Tx.Models;
using Chrysalis.Tx.Models.Cbor;
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
        PlutusConstr data = PlutusConstr.Create(alternative, CborList.Create<IPlutusData>([]));
        Assert.Equal(Convert.FromHexString(hex), data.Raw.ToArray());
        PlutusDataConstr vm = Assert.IsType<PlutusDataConstr>(ScriptContextBuilder.CodecPlutusDataToVm(IPlutusData.Read(data.Raw)));
        Assert.Equal(new BigInteger(alternative), vm.Tag);
    }

    [Fact]
    public void DeepDatumConvertsWithoutRecursion()
    {
        // 16,383 is the bare maxTxSize upper bound (synthetic 32,767-byte datum); a datum nested
        // this deep overflows the stack on HEAD and must convert without recursion.
        const int depth = 16_383;
        byte[] bytes = DeepBytes(depth);
        PlutusData converted = OnDefaultStack(() => ScriptContextBuilder.CodecPlutusDataToVm(IPlutusData.Read(bytes)));
        Assert.True(SameData(Deep(depth, 0), converted), $"Converted deep datum did not match expected shape at depth {depth}.");
        Assert.False(SameData(Deep(depth, 1), converted), $"Deep comparison must reach leaf at depth {depth}.");
    }

    [Fact]
    public void DeepDatumEvaluatesThroughEvaluateTx()
    {
        const int depth = 16_383;
        byte[] bytes = DeepBytes(depth);
        // Node form of DeepBytes(16_383): 5,461 triples of (9F..FF 2 B, D8799F..FF 4 B, A100 2 B) + leaf 00 (1 B).
        int expectedLength = (depth / 3 * 8) + 1;
        string source =
            $"(program 1.1.0 (lam ctx [(lam d (force [(force (builtin ifThenElse)) [(builtin equalsData) d d] " +
            $"(delay (force [(force (builtin ifThenElse)) [(builtin equalsInteger) [(builtin lengthOfByteString) [(builtin serialiseData) d]] (con integer {expectedLength})] " +
            "(delay (con unit ())) (delay (error))])) (delay (error))])) " +
            "[(force (builtin headList)) [(force (force (builtin sndPair))) [(builtin unConstrData) " +
            "[(force (builtin headList)) [(force (builtin tailList)) [(force (force (builtin sndPair))) [(builtin unConstrData) " +
            "[(force (builtin headList)) [(force (builtin tailList)) [(force (builtin tailList)) [(force (force (builtin sndPair))) [(builtin unConstrData) ctx]]]]]]]]]]]]]))";
        PlutusV3Script script = PlutusV3Script.Create(3, TestScripts.Wrap(source));
        byte[] scriptAddress = [0x70, .. Convert.FromHexString(script.HashHex())];
        TransactionInput outref = TransactionInput.Create(new byte[32], 0);
        PostAlonzoTransactionOutput utxo = PostAlonzoTransactionOutput.Create(
            new Address(scriptAddress), Lovelace.Create(5_000_000), InlineDatumOption.Create(1, new CborEncodedValue(bytes)));
        TransactionBuilder builder = new TransactionBuilder()
            .AddInput(new InputBuilder(outref, utxo).PlutusScriptInlineDatum(script, PlutusInt.Create(0)))
            .AddOutput([0x60, .. new byte[28]], Lovelace.Create(4_800_000))
            .SetFee(200_000);
        IReadOnlyList<EvaluationResult> results = OnDefaultStack(() =>
            ScriptContextBuilder.EvaluateTx(builder.BuildBody(), builder.BuildWitnessSet(), [new ResolvedInput(outref, utxo)], SlotNetworkConfig.Preview));
        EvaluationResult eval = Assert.Single(results);
        Assert.True(eval.ExUnits.Mem > 0 && eval.ExUnits.Steps > 0);
    }

    [Theory]
    [InlineData("80")]
    [InlineData("d8799f0120ff")]
    [InlineData("d9050082820040a0")]
    [InlineData("d8668218c89f80a1d87980429999ff")]
    [InlineData("a2019f0203ff80a10001")]
    public void ConvertsLikeTheVmReader(string hex)
    {
        byte[] bytes = Convert.FromHexString(hex);
        PlutusData expected = CborReader.DecodePlutusData(bytes);
        PlutusData converted = ScriptContextBuilder.CodecPlutusDataToVm(IPlutusData.Read(bytes));
        Assert.True(SameData(expected, converted), $"Converted datum differed from VM reader for {hex}.");
    }

    // Builds the synthetic deep datum: by level % 3 from the outside, a one-item list,
    // constr 0 of one field, or a one-entry map {0: next}, ending at leaf I 0.
    internal static byte[] DeepBytes(int depth)
    {
        byte[] bytes = new byte[(2 * depth) + 1];
        int pos = 0;
        for (int i = 0; i < depth; i++)
        {
            ReadOnlySpan<byte> level = (i % 3) switch
            {
                0 => [0x81],
                1 => [0xD8, 0x79, 0x81],
                _ => [0xA1, 0x00],
            };
            level.CopyTo(bytes.AsSpan(pos));
            pos += level.Length;
        }

        Assert.Equal(bytes.Length - 1, pos);
        return bytes;
    }

    // Runs on a fresh Thread of the runtime's default stack size so the main thread's stack cannot mask overflow.
    internal static T OnDefaultStack<T>(Func<T> run)
    {
        T? result = default;
        ExceptionDispatchInfo? error = null;
        Thread thread = new(() =>
        {
            try
            {
                result = run();
            }
            catch (Exception ex)
            {
                error = ExceptionDispatchInfo.Capture(ex);
            }
        });
        thread.Start();
        thread.Join();
        error?.Throw();
        return result!;
    }

    // Builds the VM data matching DeepBytes(depth) from the leaf out.
    private static PlutusData Deep(int depth, int leaf)
    {
        PlutusData current = new PlutusDataInteger(new BigInteger(leaf));
        PlutusData zero = new PlutusDataInteger(BigInteger.Zero);
        for (int i = depth - 1; i >= 0; i--)
        {
            current = (i % 3) switch
            {
                0 => new PlutusDataList([current]),
                1 => new PlutusDataConstr(BigInteger.Zero, [current]),
                _ => new PlutusDataMap([(zero, current)]),
            };
        }

        return current;
    }

    // Walks two VM values iteratively and checks today's conversion flags on the converted side.
    private static bool SameData(PlutusData expected, PlutusData converted)
    {
        Stack<(PlutusData Expected, PlutusData Converted)> pairs = new();
        pairs.Push((expected, converted));
        while (pairs.Count > 0)
        {
            (PlutusData left, PlutusData right) = pairs.Pop();
            switch (left, right)
            {
                case (PlutusDataConstr a, PlutusDataConstr b)
                    when a.Tag == b.Tag && a.Fields.Length == b.Fields.Length && !b.IsDefinite:
                    for (int i = 0; i < a.Fields.Length; i++)
                    {
                        pairs.Push((a.Fields[i], b.Fields[i]));
                    }

                    break;
                case (PlutusDataList a, PlutusDataList b)
                    when a.Values.Length == b.Values.Length && b.IsDefinite == b.Values.IsEmpty:
                    for (int i = 0; i < a.Values.Length; i++)
                    {
                        pairs.Push((a.Values[i], b.Values[i]));
                    }

                    break;
                case (PlutusDataMap a, PlutusDataMap b)
                    when a.Entries.Length == b.Entries.Length:
                    for (int i = 0; i < a.Entries.Length; i++)
                    {
                        pairs.Push((a.Entries[i].Key, b.Entries[i].Key));
                        pairs.Push((a.Entries[i].Value, b.Entries[i].Value));
                    }

                    break;
                case (PlutusDataInteger a, PlutusDataInteger b) when a.Value == b.Value:
                    break;
                case (PlutusDataByteString a, PlutusDataByteString b)
                    when a.Value.Span.SequenceEqual(b.Value.Span):
                    break;
                default:
                    return false;
            }
        }

        return true;
    }
}
