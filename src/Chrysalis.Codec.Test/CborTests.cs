using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Attributes;
using Chrysalis.Codec.Types.Cardano.Core;
using Chrysalis.Codec.Types.Cardano.Core.Common;
using Chrysalis.Codec.Types.Cardano.Core.Protocol;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;

namespace Chrysalis.Codec.Test;


internal record TestRecord(string P, int I);

// Test record with required fields - all fields must be present
[CborSerializable]
[CborList]
public readonly partial record struct PersonRequired : ICborType
{
    [CborOrder(0)] public partial int Id { get; }
    [CborOrder(1)] public partial string Name { get; }
    [CborOrder(2)] public partial int Age { get; }
}

// Test record with nullable fields - fields can be missing
[CborSerializable]
[CborList]
public readonly partial record struct PersonOptional : ICborType
{
    [CborOrder(0)] public partial int? Id { get; }
    [CborOrder(1)] public partial string? Name { get; }
    [CborOrder(2)] public partial int? Age { get; }
}


public class CborTests
{
    private static byte[] LoadTestBlock(string filename)
    {
        string hex = File.ReadAllText(Path.Combine("TestData", filename)).Trim();
        return Convert.FromHexString(hex);
    }

    [Fact]
    public void TransactionDeserializeAndSerialize()
    {
        string unsignedTx = "84A300D90102828258200177512CA37FED793D93474F0F8F1898E2B747ADE8D7C5F7572C64B478C5ACD00182582099DA5D9C5F595FC49AE2568E38919596940EE3C394421C27C643EE7AAA69F390000182A300581D70FA74D35F4B0D48AC6288AC5CEA71F28E807565AD09845232F229660F011A05F5E100028201D8185854D8799FD8799FD8799FD8799F581C5C5C318D01F729E205C95EB1B02D623DD10E78EA58F72D0C13F892B2FFFFD8799F40401A05F5E100FFD8799F40401A05F5E100FFD8799F40401A004C4B40FF00D8799FFFFFFF825839005C5C318D01F729E205C95EB1B02D623DD10E78EA58F72D0C13F892B2E8904EDC699E2F0CE7B72BE7CEC991DF651A222E2AE9244EB5975CBA1B0000000137EF0544021A00030AA9A0F5F6";

        PostMaryTransaction tx = CborSerializer.Deserialize<PostMaryTransaction>(Convert.FromHexString(unsignedTx));

        Assert.NotNull(tx.Body);
        Assert.NotNull(tx.Witnesses);

        string serializedHex = Convert.ToHexString(CborSerializer.Serialize(tx));
        Assert.Equal(unsignedTx, serializedHex, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void RequiredFieldValidation_ShouldSucceedWhenAllRequiredFieldsPresent()
    {
        // Create a PersonRequired with all fields
        PersonRequired requiredPerson = PersonRequired.Create(1, "John", 25);
        byte[] cbor = CborSerializer.Serialize(requiredPerson);

        // Should succeed - all required fields are present
        PersonRequired deserialized = CborSerializer.Deserialize<PersonRequired>(cbor);
        Assert.Equal(1, deserialized.Id);
        Assert.Equal("John", deserialized.Name);
        Assert.Equal(25, deserialized.Age);
    }

    [Fact]
    public void RequiredFieldValidation_ShouldSucceedWhenDeserializingRequiredAsOptional()
    {
        // Create a PersonRequired with all fields
        PersonRequired requiredPerson = PersonRequired.Create(1, "John", 25);
        byte[] cbor = CborSerializer.Serialize(requiredPerson);

        // Should succeed when deserializing as optional - all fields present
        PersonOptional optionalPerson = CborSerializer.Deserialize<PersonOptional>(cbor);
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

        IEnumerable<Task> tasks = [.. Enumerable.Range(0, concurrencyLevel).Select(__ => Task.Run(() =>
            {
                for (int i = 0; i < iterationsPerTask; i++)
                {
                    BabbageBlock block = CborSerializer.Deserialize<BabbageBlock>(cborRaw);
                    byte[] serialized = CborSerializer.Serialize(block);
                    _ = Convert.ToHexString(serialized);
                }
            }))];

        // Await all tasks to complete; if any fail, the test will fail
        await Task.WhenAll(tasks).ConfigureAwait(true);
    }

    [Theory]
    // Alonzo
    [InlineData("alonzo.block")]
    public async Task DeserializeAlonzoBlock(string cbor)
    {
        byte[] cborRaw = LoadTestBlock(cbor);

        const int concurrencyLevel = 1;   // Number of parallel tasks
        const int iterationsPerTask = 1; // Number of deserializations per task

        IEnumerable<Task> tasks = [.. Enumerable.Range(0, concurrencyLevel).Select(__ => Task.Run(() =>
        {
            for (int i = 0; i < iterationsPerTask; i++)
            {
                AlonzoCompatibleBlock block = CborSerializer.Deserialize<AlonzoCompatibleBlock>(cborRaw);
                byte[] serialized = CborSerializer.Serialize(block);
                _ = Convert.ToHexString(serialized);
            }
        }))];

        // Await all tasks to complete; if any fail, the test will fail
        await Task.WhenAll(tasks).ConfigureAwait(true);
    }

    [Theory]
    // Conway
    [InlineData("conway.block")]
    public async Task DeserializeConwayBlock(string cbor)
    {
        byte[] cborRaw = LoadTestBlock(cbor);

        const int concurrencyLevel = 1;   // Number of parallel tasks
        const int iterationsPerTask = 2; // Number of deserializations per task

        IEnumerable<Task> tasks = [.. Enumerable.Range(0, concurrencyLevel).Select(__ => Task.Run(() =>
        {
            for (int i = 0; i < iterationsPerTask; i++)
            {
                ConwayBlock block = CborSerializer.Deserialize<ConwayBlock>(cborRaw);
                byte[] serialized = CborSerializer.Serialize(block);
                _ = Convert.ToHexString(serialized);
            }
        }))];

        // Await all tasks to complete; if any fail, the test will fail
        await Task.WhenAll(tasks).ConfigureAwait(true);
    }

    [Theory]
    [InlineData("babbage-failing.block")]
    [InlineData("babbage-notsupported.block")]
    public void DeserializeFailingBabbageBlock(string filename)
    {
        byte[] cborRaw = LoadTestBlock(filename);
        BlockWithEra block = CborSerializer.Deserialize<BlockWithEra>(cborRaw);
        Assert.Equal(6, block.EraNumber);
    }

    [Theory]
    // Alonzo with duplicate key
    [InlineData("alonzo-duplicate-key.block")]
    public async Task DeserializeAlonzoWithDuplicateKey(string cbor)
    {
        byte[] cborRaw = LoadTestBlock(cbor);

        const int concurrencyLevel = 1;   // Number of parallel tasks
        const int iterationsPerTask = 1; // Number of deserializations per task

        IEnumerable<Task> tasks = [.. Enumerable.Range(0, concurrencyLevel).Select(__ => Task.Run(() =>
        {
            for (int i = 0; i < iterationsPerTask; i++)
            {
                AlonzoCompatibleBlock block = CborSerializer.Deserialize<AlonzoCompatibleBlock>(cborRaw);
                byte[] serialized = CborSerializer.Serialize(block);
                _ = Convert.ToHexString(serialized);
            }
        }))];

        // Await all tasks to complete; if any fail, the test will fail
        await Task.WhenAll(tasks).ConfigureAwait(true);
    }

    [Fact]
    public void Create_TransactionInput_RoundTrip()
    {
        byte[] txIdBytes =
        [
            0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
            0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10,
            0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18,
            0x19, 0x1A, 0x1B, 0x1C, 0x1D, 0x1E, 0x1F, 0x20
        ];
        ReadOnlyMemory<byte> txId = txIdBytes;
        ulong index = 42;

        TransactionInput input = TransactionInput.Create(txId, index);

        Assert.True(input.TransactionId.Span.SequenceEqual(txId.Span));
        Assert.Equal(index, input.Index);

        // Verify serialization round-trip
        byte[] serialized = CborSerializer.Serialize(input);
        TransactionInput deserialized = CborSerializer.Deserialize<TransactionInput>(serialized);
        Assert.True(deserialized.TransactionId.Span.SequenceEqual(txId.Span));
        Assert.Equal(index, deserialized.Index);
    }

    [Fact]
    public void Create_ExUnits_RoundTrip()
    {
        ulong mem = 1_000_000;
        ulong steps = 2_000_000;

        ExUnits exUnits = ExUnits.Create(mem, steps);

        Assert.Equal(mem, exUnits.Mem);
        Assert.Equal(steps, exUnits.Steps);

        byte[] serialized = CborSerializer.Serialize(exUnits);
        ExUnits deserialized = CborSerializer.Deserialize<ExUnits>(serialized);
        Assert.Equal(mem, deserialized.Mem);
        Assert.Equal(steps, deserialized.Steps);
    }

    [Fact]
    public void Create_Lovelace_RoundTrip()
    {
        ulong amount = 5_000_000;

        Lovelace lovelace = Lovelace.Create(amount);

        Assert.Equal(amount, lovelace.Amount);

        byte[] serialized = CborSerializer.Serialize(lovelace);
        Lovelace deserialized = CborSerializer.Deserialize<Lovelace>(serialized);
        Assert.Equal(amount, deserialized.Amount);
    }

    [Fact]
    public void Create_PersonRequired_RoundTrip()
    {
        PersonRequired person = PersonRequired.Create(99, "Alice", 30);

        Assert.Equal(99, person.Id);
        Assert.Equal("Alice", person.Name);
        Assert.Equal(30, person.Age);

        byte[] serialized = CborSerializer.Serialize(person);
        PersonRequired deserialized = CborSerializer.Deserialize<PersonRequired>(serialized);
        Assert.Equal(99, deserialized.Id);
        Assert.Equal("Alice", deserialized.Name);
        Assert.Equal(30, deserialized.Age);
    }

    [Fact]
    public void Create_PersonOptional_WithNulls_RoundTrip()
    {
        PersonOptional person = PersonOptional.Create(1, null, null);

        Assert.Equal(1, person.Id);
        Assert.Null(person.Name);
        Assert.Null(person.Age);

        byte[] serialized = CborSerializer.Serialize(person);
        PersonOptional deserialized = CborSerializer.Deserialize<PersonOptional>(serialized);
        Assert.Equal(1, deserialized.Id);
        Assert.Null(deserialized.Name);
        Assert.Null(deserialized.Age);
    }
}
