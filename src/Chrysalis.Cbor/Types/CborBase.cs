using System.Formats.Cbor;

namespace Chrysalis.Cbor.Types;

/// <summary>
/// Base class for all CBOR-serializable types
/// </summary>
/// <summary>
/// Base class for all CBOR-serializable types
/// </summary>
public abstract partial record CborBase
{
    public ReadOnlyMemory<byte>? Raw { get; set; }

    public virtual string? CborTypeName
    {
        get
        {
            Type type = GetType();

            if (type.IsGenericType)
            {
                Type genericTypeDef = type.GetGenericTypeDefinition();
                string ns = type.Namespace ?? "";
                string baseName = type.Name;

                int backtickIndex = baseName.IndexOf('`');
                if (backtickIndex > 0)
                {
                    baseName = baseName[..backtickIndex];
                }

                Type[] genericParams = genericTypeDef.GetGenericArguments();
                string typeParams = string.Join(", ", genericParams.Select(p => p.Name));

                return $"{ns}.{baseName}<{typeParams}>";
            }

            return type.FullName;
        }
    }

    public int ConstrIndex { get; set; } = 0;

    public static CborBase Read(ReadOnlyMemory<byte> data)
    {
        throw new NotImplementedException();
    }

    public static void Write(CborWriter writer, CborBase? data)
    {
        throw new NotImplementedException();
    }
}