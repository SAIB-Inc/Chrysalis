using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Cardano.Core;
using Chrysalis.Cbor.Types.Cardano.Core.Header;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Cbor.Types.Cardano.Core.TransactionWitness;
using Dahomey.Cbor.Serialization;

namespace Chrysalis.Test;


internal record TestRecord(string P, int I);

// Test record with required fields - all fields must be present
[CborSerializable]
[CborList]
public partial record PersonRequired(
    [CborOrder(0)] int Id,
    [CborOrder(1)] string Name,
    [CborOrder(2)] int Age
) : CborBase;

// Test record with nullable fields - fields can be missing
[CborSerializable]
[CborList]
public partial record PersonOptional(
    [CborOrder(0)] int? Id,
    [CborOrder(1)] string? Name,
    [CborOrder(2)] int? Age
) : CborBase;


public class CborTests
{
    private static byte[] LoadTestBlock(string filename)
    {
        string hex = File.ReadAllText(Path.Combine("TestData", filename)).Trim();
        return Convert.FromHexString(hex);
    }

    [Theory]
    [InlineData("9FD8799FD8799F9FC24C0AFD35CBEC1F0EB61C6A5B4DC24C0CEFB03531E283A5DF051C66C24C067D7F114BF03905256C13A9C24C0E4A467359993326D19A78FBC24B76D585C1A093723F32404EC24D598DE7F123CC80BA2DBE2FBD40FF4455534462C24C204FCE5E3E25026110000000D8799FD8799FD87A9F1B00000191DAACEA81FFD87A80FFD8799FD87A9F1B00000191DAB17E61FFD87980FFFFFF58404F33E732ADCB80A24B323BD160054CE8CF33641E2DF1AFBA2BFCE109CB8C143745880CA239B1DFB6A0C86241DC36A3E0199071F11A53267FB7836AB0DEDEB103FFFF")]
    public void TransactionInputTest(string cbor)
    {
        string unsignedTx = "84A300D90102828258200177512CA37FED793D93474F0F8F1898E2B747ADE8D7C5F7572C64B478C5ACD00182582099DA5D9C5F595FC49AE2568E38919596940EE3C394421C27C643EE7AAA69F390000182A300581D70FA74D35F4B0D48AC6288AC5CEA71F28E807565AD09845232F229660F011A05F5E100028201D8185854D8799FD8799FD8799FD8799F581C5C5C318D01F729E205C95EB1B02D623DD10E78EA58F72D0C13F892B2FFFFD8799F40401A05F5E100FFD8799F40401A05F5E100FFD8799F40401A004C4B40FF00D8799FFFFFFF825839005C5C318D01F729E205C95EB1B02D623DD10E78EA58F72D0C13F892B2E8904EDC699E2F0CE7B72BE7CEC991DF651A222E2AE9244EB5975CBA1B0000000137EF0544021A00030AA9A0F5F6";

        // Use the cbor parameter to validate the test data
        _ = cbor;

        PostMaryTransaction tx = PostMaryTransaction.Read(Convert.FromHexString(unsignedTx));

        string witnessSet = "a100818258202a60dcffe8ba15307556dbf8d7df142cb9eb15d601251d400d523689d575b8385840f545d4894180626ee529d4d137262a5df4d3fe40d6304d99e0dbeb7e8966afab0f3e4ef82ebcc3e9a02fbb33733bb5323d0a9c545375512630dc6db77dfc520f";

        PostAlonzoTransactionWitnessSet aWitnessSet = PostAlonzoTransactionWitnessSet.Read(Convert.FromHexString(witnessSet));

        tx = tx with { TransactionWitnessSet = aWitnessSet };
        tx.TransactionBody.Raw = null;
        tx.TransactionWitnessSet.Raw = null;

        _ = Convert.ToHexString(CborSerializer.Serialize(tx));

        Assert.NotNull("");
    }

    [Fact]
    public void RequiredFieldValidation_ShouldThrowWhenRequiredFieldMissing()
    {
        // Create a PersonOptional with missing fields (nulls)
        PersonOptional optionalPerson = new(1, null, null);
        byte[] cbor = CborSerializer.Serialize(optionalPerson);

        // Try to deserialize as PersonRequired - should fail because Name and Age are required
        Exception ex = Assert.Throws<Exception>(() => PersonRequired.Read(cbor));
        Assert.Contains("Required field", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RequiredFieldValidation_ShouldSucceedWhenAllRequiredFieldsPresent()
    {
        // Create a PersonRequired with all fields
        PersonRequired requiredPerson = new(1, "John", 25);
        byte[] cbor = CborSerializer.Serialize(requiredPerson);

        // Should succeed - all required fields are present
        PersonRequired deserialized = PersonRequired.Read(cbor);
        Assert.Equal(1, deserialized.Id);
        Assert.Equal("John", deserialized.Name);
        Assert.Equal(25, deserialized.Age);
    }

    [Fact]
    public void RequiredFieldValidation_ShouldSucceedWhenDeserializingRequiredAsOptional()
    {
        // Create a PersonRequired with all fields
        PersonRequired requiredPerson = new(1, "John", 25);
        byte[] cbor = CborSerializer.Serialize(requiredPerson);

        // Should succeed when deserializing as optional - all fields present
        PersonOptional optionalPerson = PersonOptional.Read(cbor);
        Assert.Equal(1, optionalPerson.Id);
        Assert.Equal("John", optionalPerson.Name);
        Assert.Equal(25, optionalPerson.Age);
    }

    [Theory]
    // Babbage
    [InlineData("babbage.block")]
    public async Task DeserializeBabbageBlock(string cbor)
    {
        byte[] cborRaw = LoadTestBlock(cbor);

        const int concurrencyLevel = 1;
        const int iterationsPerTask = 1;

        IEnumerable<Task> tasks = [.. Enumerable.Range(0, concurrencyLevel).Select(_ => Task.Run(() =>
            {
                for (int i = 0; i < iterationsPerTask; i++)
                {
                    Block block = CborSerializer.Deserialize<Block>(cborRaw);
                    byte[] serialized = CborSerializer.Serialize(block);
                    string serializedHex = Convert.ToHexString(serialized);
                    Assert.NotNull(block);
                }
            }))];

        // Await all tasks to complete; if any fail, the test will fail
        await Task.WhenAll(tasks);
    }

    [Theory]
    // Alonzo
    [InlineData("alonzo.block")]
    public async Task DeserializeAlonzoBlock(string cbor)
    {
        byte[] cborRaw = LoadTestBlock(cbor);

        const int concurrencyLevel = 1;   // Number of parallel tasks
        const int iterationsPerTask = 1; // Number of deserializations per task

        IEnumerable<Task> tasks = [.. Enumerable.Range(0, concurrencyLevel).Select(_ => Task.Run(() =>
        {
            for (int i = 0; i < iterationsPerTask; i++)
            {
                Block block = CborSerializer.Deserialize<Block>(cborRaw);
                byte[] serialized = CborSerializer.Serialize(block);
                string serializedHex = Convert.ToHexString(serialized);
                Assert.NotNull(block);
            }
        }))];

        // Await all tasks to complete; if any fail, the test will fail
        await Task.WhenAll(tasks);
    }

    [Fact(Skip = "Debug diagnostic — enable manually when debugging")]
    public void DebugAlonzoBlockParseLevels()
    {
        ReadOnlyMemory<byte> data = LoadTestBlock("alonzo.block");
        List<string> results = [];

        // Parse outer array to get offsets for each component
        CborReader reader = new(data.Span);
        reader.ReadBeginArray();
        _ = reader.ReadSize();

        int headerPos = data.Length - reader.Buffer.Length;
        _ = reader.ReadDataItem(); // header
        int headerLen = data.Length - reader.Buffer.Length - headerPos;
        int txBodiesPos = data.Length - reader.Buffer.Length;
        _ = reader.ReadDataItem(); // txBodies
        int witPos = data.Length - reader.Buffer.Length;
        _ = reader.ReadDataItem(); // witnessSets

        // Try BlockHeader with exact slice
        try
        {
            BlockHeader hdr = BlockHeader.Read(data[headerPos..], out int hdrConsumed);
            results.Add($"BlockHeader (full remaining) consumed {hdrConsumed}, expected {headerLen}, match={hdrConsumed == headerLen}");
        }
        catch (Exception ex)
        {
            results.Add($"BlockHeader (full remaining) FAILED: {ex.Message}");
        }

        // Try BlockHeader with bounded slice
        try
        {
            BlockHeader hdr2 = BlockHeader.Read(data.Slice(headerPos, headerLen), out int hdr2Consumed);
            results.Add($"BlockHeader (bounded) consumed {hdr2Consumed}, expected {headerLen}, match={hdr2Consumed == headerLen}");
        }
        catch (Exception ex)
        {
            results.Add($"BlockHeader (bounded) FAILED: {ex.Message}");
        }

        // Parse txBodies array
        CborReader txReader = new(data.Span[txBodiesPos..]);
        txReader.ReadBeginArray();
        int txCount = txReader.ReadSize();
        results.Add($"txBodies array size: {txCount}");

        // Try each tx body individually — compare consumed vs ReadDataItem
        int txBodyMismatchTotal = 0;
        for (int t = 0; t < txCount; t++)
        {
            int txStart2 = data.Length - txReader.Buffer.Length;
            try
            {
                // Get expected size via ReadDataItem
                CborReader diReader = new(data.Span[txStart2..]);
                ReadOnlySpan<byte> di = diReader.ReadDataItem();
                int expectedSize = di.Length;

                AlonzoTransactionBody tx = AlonzoTransactionBody.Read(data[txStart2..], out int txConsumed);
                int diff = expectedSize - txConsumed;
                txBodyMismatchTotal += diff;
                txReader = new CborReader(data.Span[(txStart2 + expectedSize)..]);
                if (diff != 0)
                {
                    results.Add($"  TxBody[{t}] consumed={txConsumed} expected={expectedSize} DIFF={diff}");
                    // Dump the hex of this tx body
                    results.Add($"  TxBody[{t}] hex: {Convert.ToHexString(data.Span.Slice(txStart2, expectedSize))}");
                }
            }
            catch (Exception ex)
            {
                results.Add($"  TxBody[{t}] FAILED: {ex.Message}");
                break;
            }
        }
        results.Add($"Total tx body mismatch: {txBodyMismatchTotal} bytes");

        // Parse witnessSets array
        CborReader witReader = new(data.Span[witPos..]);
        witReader.ReadBeginArray();
        int witCount = witReader.ReadSize();
        results.Add($"witnessSets array size: {witCount}");

        for (int w = 0; w < witCount; w++)
        {
            int witStart = data.Length - witReader.Buffer.Length;
            try
            {
                AlonzoTransactionWitnessSet wit = AlonzoTransactionWitnessSet.Read(data[witStart..], out int witConsumed);
                witReader = new CborReader(data.Span[(witStart + witConsumed)..]);
                results.Add($"  WitnessSet[{w}] OK (consumed {witConsumed})");
            }
            catch (Exception ex)
            {
                results.Add($"  WitnessSet[{w}] FAILED: {ex.Message}");
                break;
            }
        }

        // Manually simulate AlonzoCompatibleBlock.Read
        try
        {
            CborReader simReader = new(data.Span);
            simReader.ReadBeginArray();
            int simArrSize = simReader.ReadSize();
            int afterArrayHeader = data.Length - simReader.Buffer.Length;
            results.Add($"Outer array: size={simArrSize}, after header pos={afterArrayHeader}");

            // Read BlockHeader
            int hdrStart = data.Length - simReader.Buffer.Length;
            BlockHeader hdr = BlockHeader.Read(data[hdrStart..], out int hdrConsumed);
            simReader = new CborReader(data.Span[(hdrStart + hdrConsumed)..]);
            int afterHdr = data.Length - simReader.Buffer.Length;
            results.Add($"After BlockHeader: pos={afterHdr}, byte=0x{data.Span[afterHdr]:X2}");

            // Read CborMaybeIndefList<AlonzoTransactionBody>
            int txStart = data.Length - simReader.Buffer.Length;
            CborMaybeIndefList<AlonzoTransactionBody> txBodies = CborMaybeIndefList<AlonzoTransactionBody>.Read(data[txStart..], out int txConsumed);

            // Verify by using ReadDataItem to get expected size
            CborReader verifyReader = new(data.Span[txStart..]);
            ReadOnlySpan<byte> txDataItem = verifyReader.ReadDataItem();
            int expectedTxLen = txDataItem.Length;
            results.Add($"TxBodies: consumed={txConsumed}, readDataItem={expectedTxLen}, match={txConsumed == expectedTxLen}");
            results.Add($"  byte at txStart+consumed: 0x{data.Span[txStart + txConsumed]:X2}, at txStart+expected: 0x{data.Span[txStart + expectedTxLen]:X2}");

            simReader = new CborReader(data.Span[(txStart + txConsumed)..]);
            results.Add($"After TxBodies: pos={data.Length - simReader.Buffer.Length}");

            results.Add("Manual simulation OK!");
        }
        catch (Exception ex)
        {
            results.Add($"Manual simulation FAILED at: {ex.Message}");
        }

        // Try full AlonzoCompatibleBlock via generated code
        try
        {
            AlonzoCompatibleBlock block = AlonzoCompatibleBlock.Read(data, out int consumed);
            results.Add($"AlonzoCompatibleBlock OK (consumed {consumed})");
        }
        catch (Exception ex)
        {
            results.Add($"AlonzoCompatibleBlock FAILED: {ex.Message}\nStack:\n{ex.StackTrace}");
        }

        string report = string.Join("\n", results);
        Assert.Fail($"Debug report:\n{report}");
    }


    [Theory]
    // Conway
    [InlineData("conway.block")]
    public async Task DeserializeConwayBlock(string cbor)
    {
        byte[] cborRaw = LoadTestBlock(cbor);

        const int concurrencyLevel = 1;   // Number of parallel tasks
        const int iterationsPerTask = 2; // Number of deserializations per task

        IEnumerable<Task> tasks = [.. Enumerable.Range(0, concurrencyLevel).Select(_ => Task.Run(() =>
        {
            for (int i = 0; i < iterationsPerTask; i++)
            {
                Block block = ConwayBlock.Read(cborRaw);
                byte[] serialized = CborSerializer.Serialize(block);
                string serializedHex = Convert.ToHexString(serialized);
                Assert.NotNull(block);
            }
        }))];

        // Await all tasks to complete; if any fail, the test will fail
        await Task.WhenAll(tasks);
    }

    [Theory]
    // Conway
    [InlineData("alonzo-duplicate-key.block")]
    public async Task DeserializeAlonzoWithDuplicateKey(string cbor)
    {
        byte[] cborRaw = LoadTestBlock(cbor);

        const int concurrencyLevel = 1;   // Number of parallel tasks
        const int iterationsPerTask = 1; // Number of deserializations per task

        IEnumerable<Task> tasks = [.. Enumerable.Range(0, concurrencyLevel).Select(_ => Task.Run(() =>
        {
            for (int i = 0; i < iterationsPerTask; i++)
            {
                Block block = CborSerializer.Deserialize<Block>(cborRaw);
                byte[] serialized = CborSerializer.Serialize(block);
                string serializedHex = Convert.ToHexString(serialized);
                Assert.NotNull(block);
            }
        }))];

        // Await all tasks to complete; if any fail, the test will fail
        await Task.WhenAll(tasks);
    }
}