using System.Formats.Cbor;
using System.Reflection;
using Chrysalis.Cbor.Attributes;

namespace Chrysalis.Cbor.Utils;

public static class CborTagUtils
{
    // Generic versions
    public static void WriteTagIfPresent<T>(CborWriter writer)
    {
        WriteTagIfPresent(writer, typeof(T));
    }

    public static void ReadAndVerifyTag<T>(CborReader reader)
    {
        ReadAndVerifyTag(reader, typeof(T));
    }

    // Non-generic versions
    public static void WriteTagIfPresent(CborWriter writer, Type type)
    {
        CborTagAttribute? tagAttr = type.GetCustomAttribute<CborTagAttribute>();
        if (tagAttr != null)
        {
            writer.WriteTag((CborTag)tagAttr.Tag);
        }
    }

    public static void ReadAndVerifyTag(CborReader reader, Type type)
    {
        CborTagAttribute? tagAttr = type.GetCustomAttribute<CborTagAttribute>();
        if (tagAttr != null)
        {
            int tag = (int)reader.ReadTag();
            if (tag != tagAttr.Tag)
            {
                throw new InvalidOperationException($"Expected tag {tagAttr.Tag} but found {tag}");
            }
        }
    }
}