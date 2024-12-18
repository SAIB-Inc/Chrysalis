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
    [InlineData("84828f1a00552f551a0197215d5820b86f5d4306d75c93a17a50141ea27cdf13a4ba70d2a1a28354c9c95e50f54fd35820d31916004403c589efba384721020e358e4e9fbc2024b3c223c2835a79ff0af458209ea0f1ff4da71b6e9dfb01a0daa02cc566d428c7a09695b23d477c438223f4f38258408f9dac9462ae8076e369a18c25dd690ff9f6b6b68e2e709b55435f457a86d92740f8d4fe014a8f4e29fbedf4c666cf02855ab6573ffe7c866f09cb278c1f55dc5850cddf14ddeb8b9a748c3afdb2a0b48f2419dee6ef457963ecc84637d7e1705be9fcf409f5b004c5fcc3ecac8cb37281a784f45af2baf1707ed00bc4a66a747da76ab76c532096d4eb7d59a297d099290182584000017b74b05a792edac959e820c42eaaef719abaca6d4fdba7df24d5075f7273de24b1ab5c4ec33ef00dfc63b01aea08061784745ec87c243284fa692b6923a258501a8fb814cebd6aae02028b87927c59b4f8feb82daa77446070cb7b35cd35641c10bab38338e69bc9c7025cdc6fcb8213128aed171514280b1c6dc58c0943dbc5e49820de760a5a9da8ff0f07747d3f0a190a8a5820e511233ffc2e1cd24bbd850d867b70b65a9f6039859566c19712fc3f91861133582001438b89d44b6fc84a63270f880d032e441da99657f8a4d2d46b9465dc42d2570018b258403e37d0dca99b35ca571bf82e35e2a48fffbacf04f13129e33e2bf908224e7527d2981aba213047e0f5c3c409b14610149456ea3494ceb2042195026a16ff320004005901c083dfb892f3dd49a3a0c9532b3f232a1c10928760a5157d4963ecb90f368668d09ea96157e1b753368c5a750974662d351e4dc9fe2241ca8d389cca022048d30b8ea3ce335a8db35c5609ff426ee9720d5849bef7ad88fb8c368e3682eec55b95f3ec334168176ee5bdb21b3e5befb5482de180d2889755b569277e9aa9fa19ac77a742b8100a6cd5a03109ff8a83bafc91c549cde0ddc1eaf7c2ed2183dc3ee2c77ee666f66b7645ae999755017a0b6cc8cda51c8a653864dcf8ad265039a1d1c440cffa55b367e8745994d8835673c32fe179f3738b13f6aec40c80abe925f77a33ce6d5e96ef41af2cb3776c0abf09303419e745024f6b77bc03dedfdfee12d41f671cf30bcb8830f251cb02157e99524c81a79dd9d4fef48b28f63342da98af5681ecfc09ef61f1062e56c8fb45539d879469ad4ac36ebff0d884bb3ed5c2a8d028ede7bf0609d0e4b5a8175909b30639303fbe56423fa6562007abf2c26157cecec922b3ccfc0e30100d1b6e804e51da70a4e3d040c11702f724e223ddc5149d7b74a247b8aede1218beaf15b2bd305cc72da203f87d7302e906f0bbda77613b3b320cf84fac6397ca73b8a5e225ca515be56a770d666fada5b47a1b354285a4008182582073af3bb77dc3be5cb08fdb78091c9ddc0bed645916656ae3904f05c367b1249d01018182584c82d818584283581c5c7d7ef14e09f2deb82bc8c43381aefa2e871760b357d7f5385e34f9a101581e581cc9a4c68c63646830c5f13c51549c6b2b52918d239ecd44c2066effcf001a833c18221a3b9aca00021a000b8199031a01973d6aa40082825820ddcd6de118dab1547a18d8036a6f4976bd76e408f8eda8ab8d028436805802fb01825820adeae72667a8c9f372ec504cd7bc0d6d26b23b90a6fb540506f05b963e12a62500018282581d612fa41eae6ceb222820ab494d8f71ee88d5de84fcb204ecee9b460da61a7735940082583901b3b68498fe2f363794533ce22d82f36c3b07adc1ecd122a6d2ae747a6320ed82eb88cdc1e889ddd9445cf19d1fa09f647335f0fb2c082b68821a3779c2f5a1581c1131301ad4b3cb7deaddbc8f03f77189082a5738c0167e1772233097a54e43617264616e6f42697473323332014f43617264616e6f4269747336313631014f43617264616e6f4269747337393634014f43617264616e6f4269747338353935014f43617264616e6f426974733838393801021a0002b829031a01973d6ea50081825820bf30608a974d09c56dd62ca10199ec11746ea2d90dbd83649d4f37c629b1ba840001818258390117d237fb8f952c995cd28f73c555adc2307322d819b7f565196ce754348144bff68f23c1386b85dea0f8425ca574b1a11e188ffaba67537c1a0048f96f021a000351d1031a019732f30682a7581c162f94554ac8c225383a2248c245659eda870eaa82d0ef25fc7dcd82a10d8100581c2075a095b3c844a29c24317a94a643ab8e22d54a3a3a72a420260af6a10d8100581c268cfc0b89e910ead22e0ade91493d8212f53f3e2164b2e4bef0819ba10d8100581c60baee25cbc90047e83fd01e1e57dc0b06d3d0cb150d0ab40bbfead1a10d8100581cad5463153dc3d24b9ff133e46136028bdc1edbb897f5a7cf1b37950ca10d8100581cb9547b8a57656539a8d9bc42c008e38d9c8bd9c8adbb1e73ad529497a10d8100581cf7b341c14cd58fca4195a9b278cce1ef402dc0e06deb77e543cd1757a10d8100190103a5008182582056374d72edc2d887538cbb4ef0cf09f4e38b6a56191e5bee5cfa01a2ad2cca6d01018282584c82d818584283581c311b14ceda9987ea2fbcf18cf664ed2a7629c2f12f33b4d0abbb1dffa101581e581c633c7e9a7d0419c8d7f4b3e0de1c69a0dab1c1681c331821e604bbf2001a46cad9321a3b9aca008258390124e8b1108ba2d0e8803b35b86861405fcf9606a7552b9e62864201ff40a8672a9ebaa1c1b69351472ad7c419817d766f558dba19d1111d6f1a013f2151021a0002af0d031a01973d7a05a1581de140a8672a9ebaa1c1b69351472ad7c419817d766f558dba19d1111d6f1a002d3e5aa40081825820862d43628cd99035f3338b5477caba8027f249dfa908cbafd2e8b77e75ed457e03018282583901b522d1f426cc6722a1e85fdc49b69d3a3730751518c5ed829d8a30bd922375ebc24194d462c3f47d2f15817174365caf7717b095d8d38ac71a000f424082583901ab078f52254ca51b8624c1887bd01b0dac670427534ff321b5972c37427a585810a0ce94897383f8f778a1761961c7658cea2f3aba339e701a05d33bdd021a00029a03031a01972f3885a10081825820fccdf0fd58161f9a3b9c69b1ffca3f2d6929c2f5c2ec0b6b45476ed311b8b68458408b8a79122ed451628dfba2e382458c43cd015759e60b53727fbf66b398ebdc6737f02663f1f2e1db0c660017b6aa4d312345330ee6df4303211fa12e22fa570da1008282582019c4d4dfe9ee111166d5720bce7e1a2e5765c7217aaa789e94d3f4d38dd1641658406723867e7c51eb423c48b60d85d3fb5031c0a93e2964b8bb7648f8ba598d858f059951466d283affe186c0c764caf6cc4131b0efdcb80d0bbfff192570b36e078258203cf14e0f2c4790ebb530ce523f0e19cebdc6d850c0009ac5c4ccb8a5253abca95840ba94477873a04c821adbe127c0231805f52f4002705205b5d6d59ed9ab886470f24771e94cde2e096597bd38ddffb1b71d538ab8881bc656cfe501c7cfba830fa1008882582061261a95b7613ee6bf2067dad77b70349729b0c50d57bc1cf30de0db4a1e73a858407d72721e7504e12d50204f7d9e9d9fe60d9c6a4fd18ad629604729df4f7f3867199b62885623fab68a02863e7877955ca4a56c867157a559722b7b350b668a0b8258209180d818e69cd997e34663c418a648c076f2e19cd4194e486e159d8580bc6cda5840af668e57c98f0c3d9b47c66eb9271213c39b4ea1b4d543b0892f03985edcef4216d1f98f7b731eedc260a2154124b5cab015bfeaf694d58966d124ad2ff60f0382582089c29f8c4af27b7accbe589747820134ebbaa1caf3ce949270a3d0c7dcfd541b58401ad69342385ba6c3bef937a79456d7280c0d539128072db15db120b1579c46ba95d18c1fa073d7dbffb4d975b1e02ebb7372936940cff0a96fce950616d2f504825820f14f712dc600d793052d4842d50cefa4e65884ea6cf83707079eb8ce302efc855840638f7410929e7eab565b1451effdfbeea2a8839f7cfcc4c4483c4931d489547a2e94b73e4b15f8494de7f42ea31e573c459a9a7e5269af17b0978e70567de80e8258208b53207629f9a30e4b2015044f337c01735abe67243c19470c9dae8c7b73279858400c4ed03254c33a19256b7a3859079a9b75215cad83871a9b74eb51d8bcab52911c37ea5c43bdd212d006d1e6670220ff1d03714addf94f490e482edacbb08f068258205fddeedade2714d6db2f9e1104743d2d8d818ecddc306e176108db14caadd4415840bf48f5dd577b5cb920bfe60e13c8b1b889366c23e2f2e28d51814ed23def3a0ff4a1964f806829d40180d83b5230728409c1f18ddb5a61c44e614b823bd43f01825820cbc6b506e94fbefe442eecee376f3b3ebaf89415ef5cd2efb666e06ddae48393584089bff8f81a20b22f2c3f8a2288b15f1798b51f3363e0437a46c0a2e4e283b7c1018eba0b2b192d6d522ac8df2f2e95b4c8941b387cda89857ab0ae77db14780c825820e8c03a03c0b2ddbea4195caf39f41e669f7d251ecf221fbb2f275c0a5d7e05d158402643ac53dd4da4f6e80fb192b2bf7d1dd9a333bbacea8f07531ba450dd8fb93e481589d370a6ef33a97e03b2f5816e4b2c6a8abf606a859108ba6f416e530d07a100828258203c892c116b01b9c29f1a4e62b0c0aed425228d951f4adee9ca1a34b517a9b08258407cec1537a0b490c490b64169a6d6e73b6ce084d9edc48c6ad51250f27be3cf25b305fb4bb26db2ef319afe3acb2247cd7ee52b472a3ab2152be13b01fa346009825820974d020cb3cffae694aa664b3e050df9d2a47e228ca810401e18dcb145a5a0ac5840564a4f500bd6d52a21aa93c88fea83e9d3c6b6bdcec0908a4d57ea3271e93b983c33685a575d374ff4784964fbfb560b3176925781a54fc6bbeec0c2418c0809a10281845820c0179ec7663218e404dc574e85b020f4a9adf3de6a3ad974e288030cedd453fa5840530fae25fa213093411fe6bfb0171207ab58cdea2ce265c486fbba5f04d7c10032500fd06aff6ba64bd5198bc0a039bf9c5808e9eb4ee65b0129e525d92e3a0c582018a536c9c2e8db8e923546ac909e577b9e223a369fce103b336089a778c5b7cf41a0a0")]
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
                Block block = CborSerializer.Deserialize<Block>(cborRaw);

                // If block is occasionally null or exceptions occur, it's a sign of a concurrency issue
                Assert.NotNull(block);
            }
        })).ToList();

        // Await all tasks to complete; if any fail, the test will fail
        await Task.WhenAll(tasks);
    }
}
