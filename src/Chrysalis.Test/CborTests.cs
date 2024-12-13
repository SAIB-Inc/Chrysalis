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
    [InlineData("D8799FA44A61747472696275746573B81F4C41746D6F73706865726963734A4669726520536D6F6B654742616C6472696355426C61636B20476F6C642043726F7373626F6E657344426173654946656D616C6520303745436F617473544C656174686572205472656E636820426C61636B4A436F696E20506F756368444E6F6E6547436F6D706173734642726F6B656E45436F776C734A436C6F746820476F6C6448456172204C65667446566F6F646F6F494561722052696768744B54726962616C20426F6E65444579657344426C75654F4579657320416573746865746963735052756E6564204379626572204C6566744A46616365204D61726B73464D6964646C654646616D696C794C57696C6C69616D204B69646447466F7274756E6542343744486169724F4379626572204375742057686974654B486F6C7374657220546F70534B72616B656E20476F6C64204469616D6F6E64444974656D444E6F6E654F4C6566742041726D20546174746F6F4B4465616420506972617465464D6F72616C6541304A4E616C62696E64696E67445475736B4A4E617669676174696F6E4235354B4E65636B20546174746F6F534F72646572206F6620746865204B72616B656E4452616E6B4943617270656E7465724A526573696C69656E63654235345052696768742041726D20546174746F6F4947656F6D65747269634352756D4852756D2046756C6C465368697274734B46656C667420436F7665724653776F7264734D506C61696E204375746C61737348576178205365616C4C476F6C6420576178204F544B4A576561706F6E20546F7052526F626572742773205265636B6F6E696E674B6261636B67726F756E64734B536B79206F6E20466972654B6465736372697074696F6E9F58394F544B20697320616E20656E64206F662074686520776F726C642C206675747572697374696320706972617465207468656D6564204E4654205825636F6C6C656374696F6E206F6E207468652043617264616E6F20626C6F636B636861696E2EFF45696D6167655835697066733A2F2F516D66356E42336E4B5870666A453636566B724873654D37515854584337707270465934555A54534A366A387233446E616D654C50697261746520233134313501FF")]
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
