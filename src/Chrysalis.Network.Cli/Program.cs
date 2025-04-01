using System.Formats.Cbor;
using System.Text.Json;
using Chrysalis.Cbor.Types.Cardano.Core;
using Chrysalis.Cbor.Types.Cardano.Core.Header;
using Chrysalis.Network.Cbor.ChainSync;
using Chrysalis.Network.Cbor.Common;
using Chrysalis.Network.Cbor.Handshake;
using Chrysalis.Network.Cli;
using Chrysalis.Network.MiniProtocols.Extensions;
using Chrysalis.Network.Multiplexer;


NodeClient client = await NodeClient.ConnectAsync("/home/rjlacanlale/cardano/ipc/node.socket");
client.Start();

ProposeVersions proposeVersion = HandshakeMessages.ProposeVersions(VersionTables.N2C_V10_AND_ABOVE());
CborWriter writer = new();
ProposeVersions.Write(writer, proposeVersion);
string serialized = Convert.ToHexString(writer.Encode());

Console.WriteLine("Sending handshake message...");
await client.Handshake!.SendAsync(proposeVersion, CancellationToken.None);
Console.WriteLine("Handshake success!!");

Point point = new(57371845, Convert.FromHexString("20a81db38339bf6ee9b1d7e22b22c0ac4d887d332bbf4f3005db4848cd647743"));

Console.WriteLine("Finding Intersection...");
await client.ChainSync!.FindIntersectionAsync([point], CancellationToken.None);
Console.WriteLine("Intersection found");

Console.WriteLine("Initializing Db...");
await BlockDbHelper.InitializeDbAsync();

Console.WriteLine("Starting ChainSync...");

int blockCount = 0;
int deserializedCount = 0;
_ = Task.Run(async () =>
{
    while (true)
    {
        Console.WriteLine($"Block count: {blockCount}, Deserialized: {deserializedCount}, Success rate: {(deserializedCount > 0 ? deserializedCount / blockCount * 100.0 : 0.0)}%");
        blockCount = 0;
        deserializedCount = 0;
        await Task.Delay(1000);
    }
});

while (true)
{
    try
    {
        MessageNextResponse? nextResponse = await client.ChainSync!.NextRequestAsync(CancellationToken.None);

        switch (nextResponse)
        {
            case MessageRollBackward msg:
                Console.WriteLine($"Rolling back to {msg.Point.Slot}");
                break;
            case MessageRollForward msg:
                try
                {
                    Block? block = TestUtils.DeserializeBlockWithEra(msg.Payload.Value);
                    blockCount++;
                    deserializedCount++;

                    ulong blockNumber = 0;
                    ulong blockSlot = 0;
                    string blockHash = string.Empty;

                    switch (block)
                    {
                        case AlonzoCompatibleBlock alonzoBlock:
                            switch (alonzoBlock.Header.HeaderBody)
                            {
                                case AlonzoHeaderBody alonzoBlockHeaderBody:
                                    blockNumber = alonzoBlockHeaderBody.BlockNumber;
                                    blockSlot = alonzoBlockHeaderBody.Slot;
                                    blockHash = Convert.ToHexString(alonzoBlockHeaderBody.BlockBodyHash);
                                    break;
                                case BabbageHeaderBody babbageHeaderBody:
                                    blockNumber = babbageHeaderBody.BlockNumber;
                                    blockSlot = babbageHeaderBody.Slot;
                                    blockHash = Convert.ToHexString(babbageHeaderBody.BlockBodyHash);
                                    break;
                                default:
                                    throw new NotSupportedException($"Unsupported Alonzo block header body: {alonzoBlock.Header.HeaderBody.GetType()}");
                            }
                            break;
                        case BabbageBlock babbageBlock:
                            switch (babbageBlock.Header.HeaderBody)
                            {
                                case AlonzoHeaderBody alonzoHeaderBody:
                                    blockNumber = alonzoHeaderBody.BlockNumber;
                                    blockSlot = alonzoHeaderBody.Slot;
                                    blockHash = Convert.ToHexString(alonzoHeaderBody.BlockBodyHash);
                                    break;
                                case BabbageHeaderBody babbageHeaderBody:
                                    blockNumber = babbageHeaderBody.BlockNumber;
                                    blockSlot = babbageHeaderBody.Slot;
                                    blockHash = Convert.ToHexString(babbageHeaderBody.BlockBodyHash);
                                    break;
                                default:
                                    throw new NotSupportedException($"Unsupported Babbage block header body: {babbageBlock.Header.HeaderBody.GetType()}");
                            }
                            break;
                        case ConwayBlock conwayBlock:
                            switch (conwayBlock.Header.HeaderBody)
                            {
                                case AlonzoHeaderBody alonzoHeaderBody:
                                    blockNumber = alonzoHeaderBody.BlockNumber;
                                    blockSlot = alonzoHeaderBody.Slot;
                                    blockHash = Convert.ToHexString(alonzoHeaderBody.BlockBodyHash);
                                    break;
                                case BabbageHeaderBody babbageHeaderBody:
                                    blockNumber = babbageHeaderBody.BlockNumber;
                                    blockSlot = babbageHeaderBody.Slot;
                                    blockHash = Convert.ToHexString(babbageHeaderBody.BlockBodyHash);
                                    break;
                                default:
                                    throw new NotSupportedException($"Unsupported Conway block header body: {conwayBlock.Header.HeaderBody.GetType()}");
                            }
                            break;
                        default:
                            throw new NotSupportedException($"Unsupported block type: {block?.GetType()}");
                    }

                    await BlockDbHelper.InsertBlockAsync(blockNumber, blockSlot, blockHash);
                }
                catch
                {
                    CborReader reader = new(msg.Payload.Value, CborConformanceMode.Lax);
                    reader.ReadTag();
                    reader = new(reader.ReadByteString(), CborConformanceMode.Lax);
                    reader.ReadStartArray();
                    Era era = (Era)reader.ReadInt32();
                    ReadOnlyMemory<byte> blockBytes = reader.ReadEncodedValue(true);


                    Console.WriteLine($"Failed to deserialize block: {Convert.ToHexString(blockBytes.ToArray())}");
                    deserializedCount--;
                    throw;
                }
                break;
            case MessageAwaitReply msg:
                Console.WriteLine($"Block count: {blockCount}");
                blockCount = 0;
                deserializedCount = 0;
                Console.WriteLine("Tip reached!!!");
                break;
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine(ex);
        throw;
    }
}

public record CPoint(string Hash, ulong Slot);