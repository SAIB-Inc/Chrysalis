using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Network.Cbor.KeepAlive;

[CborSerializable]
[CborUnion]
public abstract partial record KeepAliveMessage : CborBase;

[CborSerializable]
[CborList]
[CborIndex(0)]
public partial record MessageKeepAlive(
    [CborOrder(0)] int Idx,
    [CborOrder(1)] uint Cookie
) : KeepAliveMessage;

[CborSerializable]
[CborList]
[CborIndex(1)]
public partial record MessageKeepAliveResponse(
    [CborOrder(0)] int Idx,
    [CborOrder(1)] uint Cookie
) : KeepAliveMessage;

[CborSerializable]
[CborList]
[CborIndex(2)]
public partial record MessageDone(
    [CborOrder(0)] int Idx
) : KeepAliveMessage;

public static class KeepAliveMessages
{
    public static MessageKeepAlive KeepAlive(uint cookie)
    {
        return new(0, cookie);
    }

    public static MessageKeepAliveResponse KeepAliveResponse(uint cookie)
    {
        return new(1, cookie);
    }

    public static MessageDone Done()
    {
        return new(2);
    }
}
