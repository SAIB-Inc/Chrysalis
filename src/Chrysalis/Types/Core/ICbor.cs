namespace Chrysalis.Types.Core;

public interface ICbor
{
    byte[]? Raw { get; set; }
}

public record RawCbor : ICbor
{
    public byte[]? Raw { get; set; } = default;
}