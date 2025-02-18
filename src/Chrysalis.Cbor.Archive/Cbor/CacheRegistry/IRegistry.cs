using System.Reflection;
using Chrysalis.Cbor.Converters;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.CacheRegistry;

public interface IRegistry
{
    ICborConverter GetConverter(Type type);
    CborOptions GetOptions(Type type);
    Delegate GetActivator(Type type);
    PropertyInfo[] GetProperties(Type type);
    Delegate GetGetter(Type type, string propertyName);
    Delegate GetSetter(Type type, string propertyName);
    ConstructorInfo[] GetConstructors(Type type);
    MethodInfo GetMethod(Type type, string methodName);
}
