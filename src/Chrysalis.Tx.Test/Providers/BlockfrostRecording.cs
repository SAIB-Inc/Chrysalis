using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace Chrysalis.Tx.Test.Providers;

internal static class BlockfrostRecording
{
    internal const string Address = "addr_test1wzpaveyshpnq5fn0ke3uxd329tgd509445eeqd3s2pzmk0qg9xn5l";
    internal const string AddressPage = $"addresses/{Address}/utxos?count=100&page=1";
    internal const string ReferenceTransaction = "e812fd90ba58247c07664114a94ca63e2d59a5f7881c88a55a022c46ecb0ac03";
    internal const string ReferenceScript = "83d66490b8660a266fb663c3362a2ad0da3cb5ad339036305045bb3c";
    private static string Folder => Path.Combine(AppContext.BaseDirectory, "TestData/providers/blockfrost");

    internal sealed record Row(string Method, string Path, HttpStatusCode Status, byte[] Body)
    {
        internal JsonDocument Json() => JsonDocument.Parse(Body);
    }

    internal static Dictionary<string, Row> Load(string? folder = null)
    {
        folder ??= Folder;
        Dictionary<string, Row> rows = [];
        foreach (string line in File.ReadLines(Path.Combine(folder, "manifest.tsv"))
            .Where(line => !line.StartsWith('#', StringComparison.Ordinal)).Skip(1))
        {
            string[] fields = line.Split('\t');
            Assert.Equal(7, fields.Length);
            string name = fields[0];
            Assert.Equal(name, Path.GetFileName(name));
            Assert.Equal("application/json", fields[4]);
            byte[] body = File.ReadAllBytes(Path.Combine(folder, name));
            Assert.Equal(fields[5], Convert.ToHexStringLower(SHA256.HashData(body)));
            Assert.True(rows.TryAdd(name, new(fields[1], fields[2], (HttpStatusCode)int.Parse(fields[3], CultureInfo.InvariantCulture), body)),
                $"Duplicate fixture: {name}");
        }
        string[] found = [.. Directory.EnumerateFiles(folder).Select(path => Path.GetFileName(path))
            .Where(name => name is not ("manifest.tsv" or ".gitattributes")).Order(StringComparer.Ordinal)];
        Assert.NotEmpty(rows);
        Assert.Equal(rows.Keys.Order(StringComparer.Ordinal), found);
        return rows;
    }

    internal static StubHttpHandler Handler(Dictionary<string, Row> rows, params (string Path, string File)[] overrides)
    {
        Dictionary<(string Method, string Path), Row> routes = rows.Values.ToDictionary(row => (row.Method, row.Path));
        foreach ((string path, string file) in overrides)
        {
            Row row = rows[file];
            routes[(row.Method, path)] = row;
        }
        return new(request =>
        {
            string path = request.RequestUri!.PathAndQuery;
            if (path.StartsWith("/api/v0/", StringComparison.Ordinal)) { path = path["/api/v0/".Length..]; }
            Assert.True(routes.TryGetValue((request.Method.Method, path), out Row? row), $"Unrecorded {request.Method} {path}");
            ByteArrayContent content = new(row!.Body);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            return new HttpResponseMessage(row.Status) { Content = content };
        });
    }
}
