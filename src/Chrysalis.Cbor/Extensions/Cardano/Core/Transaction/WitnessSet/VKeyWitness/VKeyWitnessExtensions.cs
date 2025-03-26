using CVKeyWitness = Chrysalis.Cbor.Types.Cardano.Core.TransactionWitness.VKeyWitness;

namespace Chrysalis.Cbor.Extensions.Cardano.Core.Transaction.WitnessSet.VKeyWitness;

public static class VKeyWitnessExtensions
{
    public static byte[] VKey(this CVKeyWitness self) => self.VKey;

    public static byte[] Signature(this CVKeyWitness self) => self.Signature;
}   