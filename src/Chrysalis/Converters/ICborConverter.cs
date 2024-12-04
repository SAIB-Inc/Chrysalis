using Chrysalis.Types;

namespace Chrysalis.Converters;

public interface ICborConverter<in T> where T : ICbor
{
    byte[] Serialize(T value);
    ICbor Deserialize(byte[] data, Type? targetType = null);
}