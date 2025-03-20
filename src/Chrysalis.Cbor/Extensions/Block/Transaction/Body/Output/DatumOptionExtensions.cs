using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Output;
using static Chrysalis.Cbor.Cardano.Types.Block.Transaction.Output.DatumOption;

namespace Chrysalis.Cbor.Extensions.Block.Transaction.Body.Output;

public static class DatumOptionExtensions
{
    public static int Option(this DatumOption self) =>
        self switch
        {
            DatumHashOption datumHashOption => datumHashOption.Option,
            InlineDatumOption inlineDatumOption => inlineDatumOption.Option,
            _ => throw new NotImplementedException()
        };

    public static byte[] Data(this DatumOption self) =>
        self switch
        {
            InlineDatumOption inlineDatumOption => inlineDatumOption.Data,
            _ => []
        };
}