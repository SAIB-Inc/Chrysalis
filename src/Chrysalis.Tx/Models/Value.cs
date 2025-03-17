namespace Chrysalis.Tx.Models;

public enum AssetType
{
    Ada,
    Asset
}

public record Value(AssetType Type, ulong Amount, string Subject = "")
{
    public AssetType Type { get; init; } = Type;
    public string Subject { get; init; } = Subject;
    public ulong Amount { get; init; } = Amount;
}

