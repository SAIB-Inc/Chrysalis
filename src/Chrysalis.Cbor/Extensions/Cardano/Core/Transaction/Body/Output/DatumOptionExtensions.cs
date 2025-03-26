using Chrysalis.Cbor.Types.Cardano.Core.Common;

namespace Chrysalis.Cbor.Extensions.Cardano.Core.Transaction.Body.Output;

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
            InlineDatumOption inlineDatumOption => inlineDatumOption.Data.Value,
            _ => []
        };
}