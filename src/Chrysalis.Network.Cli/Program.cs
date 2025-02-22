using Chrysalis.Cbor.Serialization;
using Chrysalis.Network.Cbor;
using Chrysalis.Network.Core;
using Chrysalis.Network.Multiplexer;

string handshakeMessageCborHex = "8200a7078202f5088202f5098202f50a8202f50b8402f500f40c8402f500f40d8402f500f4";
byte[] handshakeMessageCborHexBytes = Convert.FromHexString(handshakeMessageCborHex);
HandshakeMessage deserializedHandshakeMessage = CborSerializer.Deserialize<HandshakeMessage>(handshakeMessageCborHexBytes);
string serializedHandshakeMessage = Convert.ToHexString(CborSerializer.Serialize(deserializedHandshakeMessage)).ToLowerInvariant();

HandshakeMessage proposeVersion = new ProposeVersions(
    new ProposeVersionId(0),
    new VersionTable(
        new(){
            { Versions.V13, new NodeToNodeVersionData(new(2), new(true),new(0), new(false))},
            { Versions.V14, new NodeToNodeVersionData(new(2), new(true),new(0), new(false))}
        }
    )
);

byte[] proposeVersionBytes = CborSerializer.Serialize(proposeVersion);


TcpBearer bearer = new("localhost", 1234);
Muxer muxer = new(bearer);
Demuxer demuxer = new(bearer);
await muxer.WriteSegmentAsync(
    ProtocolType.Handshake,
    proposeVersionBytes,
    CancellationToken.None
);