using System.Drawing;
using ChrysalisV2.Types.Core;

namespace ChrysalisV2.Attributes;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface)]
public class CborSerializableAttribute(Type cborType, int index = -1, bool isDefinite = false, int size = -1) : Attribute
{
    public Type CborType { get; } = cborType;
    public int Index { get; } = index;
    public bool IsDefinite { get; } = isDefinite;
    public bool IsCustom { get; set; } = false;
    public int Size { get; set; } = size;
}