namespace Chrysalis.Network.Multiplexer;

public record Header(
    ProtocolType Protocol,
    ushort PayloadLength
);