using Chrysalis.Network.Core;
using Chrysalis.Network.Multiplexer;

var bearer = new TcpBearer("localhost", 1234);
var muxer = new Muxer(bearer);
var demuxer = new Demuxer(bearer);
await muxer.WriteSegmentAsync(
    ProtocolType.Handshake,
    Convert.FromHexString("8200a7078202f5088202f5098202f50a8202f50b8402f500f40c8402f500f40d8402f500f4"),
    CancellationToken.None
);