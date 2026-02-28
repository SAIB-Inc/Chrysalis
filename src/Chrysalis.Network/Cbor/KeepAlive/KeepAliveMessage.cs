using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Network.Cbor.KeepAlive;

[CborSerializable]
[CborUnion]
public abstract partial record KeepAliveMessage : CborBase;

[CborSerializable]
[CborList]
public partial record MessageKeepAlive(
    [CborOrder(0)] Value0 Idx,
    [CborOrder(1)] uint Cookie
) : KeepAliveMessage;

[CborSerializable]
[CborList]
public partial record MessageKeepAliveResponse(
    [CborOrder(0)] Value1 Idx,
    [CborOrder(1)] uint Cookie
) : KeepAliveMessage;

[CborSerializable]
[CborList]
public partial record MessageDone(
    [CborOrder(0)] Value2 Idx
) : KeepAliveMessage;

public static class KeepAliveMessages
{
    public static MessageKeepAlive KeepAlive(uint cookie)
    {
        return new(new Value0(0), cookie);
    }

    public static MessageKeepAliveResponse KeepAliveResponse(uint cookie)
    {
        return new(new Value1(1), cookie);
    }

    public static MessageDone Done()
    {
        return new(new Value2(2));
    }
}
