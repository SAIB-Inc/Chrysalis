using System.Security.Cryptography;
using System.Text.Json;
using Chrysalis.Wallet.Models.Keys;
using Chrysalis.Wallet.Words;

namespace Chrysalis.Wallet.Test.Keys;

public class DerivationVectorTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void GetRootKey_MatchesCip3Icarus(int index)
    {
        JsonElement vector = ReadVectors("icarus.json")[index];
        Mnemonic mnemonic = Mnemonic.Restore(vector.GetProperty("phrase").GetString()!, English.Words);
        PrivateKey key = mnemonic.GetRootKey(vector.GetProperty("password").GetString()!);
        byte[] actual = [.. key.Key, .. key.Chaincode];
        Assert.Equal(Convert.FromHexString(vector.GetProperty("expected").GetString()!), actual);
    }

    private static JsonElement ReadVectors(string name)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "TestData", "CIPs");
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "manifest.json")));
        JsonElement entry = Assert.Single(manifest.RootElement.EnumerateArray(), row => row.GetProperty("path").GetString() == name);
        byte[] bytes = File.ReadAllBytes(Path.Combine(directory, name));
        Assert.Equal(entry.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(bytes)));
        using JsonDocument document = JsonDocument.Parse(bytes);
        return document.RootElement.Clone();
    }
}
