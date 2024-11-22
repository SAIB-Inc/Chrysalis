using Chrysalis.Converters;

namespace Chrysalis.Attributes;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface)]
public class CborSerializableAttribute : Attribute
{
    public Type? Converter { get; set; }
    public int Index { get; set; } = -1;
    public bool IsDefinite { get; set; } = false;
    public bool IsCustom { get; set; } = false;
    public int Size { get; set; } = -1;

    public CborSerializableAttribute()
    {
    }

    public CborSerializableAttribute(Type? converter = null, int index = -1, bool isDefinite = false, int size = -1)
    {
        if (converter != null)
        {
            // Validate that the converter implements ICborConverter<T>
            Type converterInterface = typeof(ICborConverter<>).MakeGenericType(converter);
            if (!converterInterface.IsAssignableFrom(converter))
            {
                throw new ArgumentException("Converter must implement ICborConverter<T>", nameof(converter));
            }
        }

        Converter = converter;
        Index = index;
        IsDefinite = isDefinite;
        Size = size;
    }
}