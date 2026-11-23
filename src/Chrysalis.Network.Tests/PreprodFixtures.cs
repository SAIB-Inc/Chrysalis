using System.Text.Json;

namespace Chrysalis.Network.Tests;

internal static class PreprodFixtures
{
    internal static byte[] Read(string name) => Read("Preprod", name);

    internal static byte[] Read(string folder, string name)
    {
        string resourceName = $"Chrysalis.Network.Tests.Fixtures.{folder}.{name.Replace('/', '.')}";
        using Stream stream = typeof(PreprodFixtures).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Missing fixture: {name}");
        using MemoryStream buffer = new();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    internal static JsonElement Manifest()
    {
        using JsonDocument document = JsonDocument.Parse(Read("manifest.json"));
        return document.RootElement.Clone();
    }

    internal static JsonElement Find(string collection, string property, string value) =>
        Manifest().GetProperty(collection).EnumerateArray().Single(entry => entry.GetProperty(property).GetString() == value);

    internal static byte[] Message(JsonElement entry)
    {
        byte[] joined = [.. entry.GetProperty("files").EnumerateArray()
            .SelectMany(file => Read(file.GetString()!))];
        return joined.AsSpan(entry.GetProperty("offset").GetInt32(), entry.GetProperty("bytes").GetInt32()).ToArray();
    }
}
