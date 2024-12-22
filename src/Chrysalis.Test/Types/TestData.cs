using Chrysalis.Cbor.Types;
using Newtonsoft.Json;
using Xunit.Abstractions;

namespace Chrysalis.Test.Types;

public class TestData : IXunitSerializable
{
    public string Description { get; set; } = string.Empty;
    public string Serialized { get; set; } = string.Empty;
    public CborBase Deserialized { get; set; } = default!;

    public TestData() { }
    public TestData(string description, string serialized, CborBase deserialized)
    {
        Description = description;
        Serialized = serialized;
        Deserialized = deserialized;
    }

    public void Deserialize(IXunitSerializationInfo info)
    {
        Description = info.GetValue<string>(nameof(Description));
        Serialized = info.GetValue<string>(nameof(Serialized));
        var typeName = info.GetValue<string>("DeserializedType");
        var json = info.GetValue<string>(nameof(Deserialized));
        var type = Type.GetType(typeName) ?? throw new InvalidOperationException("Failed to get type.");
        Deserialized = (CborBase?)JsonConvert.DeserializeObject(json, type) ?? throw new InvalidOperationException("Failed to deserialize object.");
    }

    public void Serialize(IXunitSerializationInfo info)
    {
        info.AddValue(nameof(Description), Description);
        info.AddValue(nameof(Serialized), Serialized);
        info.AddValue("DeserializedType", Deserialized.GetType().AssemblyQualifiedName);
        info.AddValue(nameof(Deserialized), JsonConvert.SerializeObject(Deserialized));
    }
}