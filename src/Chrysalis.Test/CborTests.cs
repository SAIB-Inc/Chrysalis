using System.Reflection;
using System.Text.Json;
using Chrysalis.Cardano.Core.Types.Block;
using Chrysalis.Cardano.Core.Types.Block.Transaction.Script;
using Chrysalis.Cardano.Crashr.Types.Datums;
using Chrysalis.Cbor.Converters;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Collections;
using Chrysalis.Plutus.Types;
using Chrysalis.Test.Types;
using Chrysalis.Test.Types.Cardano.Crashr;
using Chrysalis.Test.Types.Primitives;
using Xunit;

namespace Chrysalis.Test;

public class CborTests
{
    [Theory]
    [MemberData(nameof(BoolTestData.GetTestData), MemberType = typeof(BoolTestData))]
    [MemberData(nameof(BytesTestData.GetTestData), MemberType = typeof(BytesTestData))]
    [MemberData(nameof(IntTestData.GetTestData), MemberType = typeof(IntTestData))]
    [MemberData(nameof(LongTestData.GetTestData), MemberType = typeof(LongTestData))]
    [MemberData(nameof(UlongTestData.GetTestData), MemberType = typeof(UlongTestData))]
    [MemberData(nameof(TextTestData.GetTestData), MemberType = typeof(TextTestData))]
    public void Deserialize(TestData testData)
    {
        Assert.NotNull(testData);
        Assert.NotNull(testData.Serialized);
        Assert.NotNull(testData.Deserialized);

        byte[] data = Convert.FromHexString(testData.Serialized);
        Type actualType = testData.Deserialized.GetType();

        // Act
        MethodInfo deserializeMethod = typeof(CborSerializer)
            .GetMethod(nameof(CborSerializer.Deserialize))!
            .MakeGenericMethod(actualType);

        object? actual = deserializeMethod.Invoke(null, [data]);

        // Assert
        Assert.NotNull(actual);
        Assert.IsType(actualType, actual);
        // Assert.Equivalent(testData.Deserialized, actual);
    }

    [Theory]
    [MemberData(nameof(CrashrTestData.GetTestData), MemberType = typeof(CrashrTestData))]
    public void DeserializeCrashr(string testName, string serialized, CborBase deserialized)
    {
        Assert.NotNull(serialized);
        Assert.NotNull(deserialized);

        byte[] data = Convert.FromHexString(serialized);
        Type actualType = deserialized.GetType();

        // Act
        MethodInfo deserializeMethod = typeof(CborSerializer)
            .GetMethod(nameof(CborSerializer.Deserialize))!
            .MakeGenericMethod(actualType);

        object? actual = deserializeMethod.Invoke(null, [data]);

        // Assert
        Assert.NotNull(actual);
        Assert.IsType(actualType, actual);
        CrashrTestData.AssertListingDatumsEqual((ListingDatum)deserialized, (ListingDatum)actual);
    }

    [Theory]
    // conway
    [InlineData("D87983AA436167654C4170722031332C20323032334370667082584068747470733A2F2F6173736574316B783038653333343761773861353075356A747239676D66786A35786133676C61366139746D2E6170652E6E667463646E2E5840696F2F696D6167653F73697A653D32353626746B3D5F42427751364B4B4D4A38684562596E585F5F5066304F7770617369775177586E4747417A327775465F77446E616D654D436974697A656E2023373539384566696C657383A3437372635835697066733A2F2F516D573377427264645673374459714541533956366E77626A4B6D646241363269656E3276773938647172467934446E616D654D436974697A656E202337353938496D656469615479706549696D6167652F706E67A3437372635835697066733A2F2F516D5842755158696A4450776A626853486D5A57454659534231576F46563171706E634D4D634170544553703148446E616D654850617373706F7274496D656469615479706549696D6167652F676966A3437372635835697066733A2F2F516D564C4D6F414A487A6D4D75615047685A50514C524C573563796B5665435357415331396755566B5262537261446E616D654850617373706F7274496D656469615479706549766964656F2F6D703445696D6167655835697066733A2F2F516D573377427264645673374459714541533956366E77626A4B6D646241363269656E327677393864717246793446476F6C64656E4566616C7365466F726967696E4F5468652041706520536F6369657479467374616D7073427B7D496D656469615479706549696D6167652F706E674A736F6C416464726573734001D879860181581CE36F43A40751C35295B19A218301CC7BE019D016E8927C0321FD28C7D87A80581CFCA746F58ADF9F3DA13B7227E5E2C6052F376447473F4D49F8004195D87A80D87A80")]
    public async Task DeserializeBlock(string cbor)
    {
        byte[] cborRaw = Convert.FromHexString(cbor);

        const int concurrencyLevel = 1;   // Number of parallel tasks
        const int iterationsPerTask = 1; // Number of deserializations per task

        List<Task> tasks = Enumerable.Range(0, concurrencyLevel).Select(_ => Task.Run(() =>
        {
            for (int i = 0; i < iterationsPerTask; i++)
            {
                // This call should be thread-safe and never fail if the code is correct
                Cip68<PlutusData> cip68 = CborSerializer.Deserialize<Cip68<PlutusData>>(cborRaw);

                // If block is occasionally null or exceptions occur, it's a sign of a concurrency issue
                Assert.NotNull(cip68);
            }
        })).ToList();

        // Await all tasks to complete; if any fail, the test will fail
        await Task.WhenAll(tasks);
    }
}
