using Chrysalis.Cardano.Core.Types.Block.Transaction.Output;

namespace Chrysalis.Cardano.Core.Extensions;

public static class DatumExtension
{
    public static byte[]? DatumHash(this DatumOption? datumOption)
        => datumOption switch
        {
            DatumHashOption hashOption => hashOption.DatumHash.Value,
            InlineDatumOption => null,
            _ => null
        };

    public static byte[]? InlineDatum(this DatumOption? datumOption)
        => datumOption switch
        {
            InlineDatumOption inlineOption => inlineOption.Data.Value,
            DatumHashOption => null,
            _ => null
        };

    public static bool IsInline(this DatumOption? datumOption)
        => datumOption is InlineDatumOption;

    public static bool IsHash(this DatumOption? datumOption)
        => datumOption is DatumHashOption;

}