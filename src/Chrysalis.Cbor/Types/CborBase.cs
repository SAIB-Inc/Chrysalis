using System.Formats.Cbor;

namespace Chrysalis.Cbor.Types;

/// <summary>
/// Base class for all CBOR-serializable types
/// </summary>
/// <summary>
/// Base class for all CBOR-serializable types
/// </summary>
public abstract partial record CborBase<T> where T : CborBase<T>
{
    // Raw serialized data for caching and quick re-serialization
    public ReadOnlyMemory<byte>? Raw { get; set; }

    // Type discriminator for unions (to avoid reflection)
    // save the fully qualified name of the type
    public virtual string? CborTypeName
    {
        get
        {
            Type type = GetType();

            if (type.IsGenericType)
            {
                // Get the generic type definition
                Type genericTypeDef = type.GetGenericTypeDefinition();

                // Get the namespace and simple type name
                string ns = type.Namespace ?? "";
                string baseName = type.Name;

                // Remove the `n and everything after it from the name
                int backtickIndex = baseName.IndexOf('`');
                if (backtickIndex > 0)
                {
                    baseName = baseName.Substring(0, backtickIndex);
                }

                // Get the generic parameter names from the type definition
                Type[] genericParams = genericTypeDef.GetGenericArguments();
                string typeParams = string.Join(", ", genericParams.Select(p => p.Name));

                return $"{ns}.{baseName}<{typeParams}>";
            }

            return type.FullName;
        }
    }

    public int ConstrIndex { get; set; } = 0;

    public static T Read(ReadOnlyMemory<byte> data)
    {
        throw new NotImplementedException("Read method not implemented for " + typeof(T).Name);
    }

    public static ReadOnlyMemory<byte> Write(CborWriter writer, T value)
    {
        throw new NotImplementedException("Write method not implemented for " + typeof(T).Name);
    }
}