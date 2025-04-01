using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Cardano.Core.Protocol;

namespace Chrysalis.Tx.Models;

public enum RedeemerTag
{
    Spend = 0,
    Mint = 1,
    Cert = 2,
    Reward = 3,
    Vote = 4,
    Propose = 5,

}

public delegate TData RedeemerDataBuilder<TContext, TData>(InputOutputMapping mapping, TContext context) where TData : CborBase;

public record Redeemer<T>(RedeemerTag Tag, ulong Index, T Data, ExUnits ExUnits) where T : CborBase;
