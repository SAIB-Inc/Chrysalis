using CBootstrapWitness = Chrysalis.Cbor.Types.Cardano.Core.TransactionWitness.BootstrapWitness;

namespace Chrysalis.Cbor.Extensions.Cardano.Core.Transaction.WitnessSet.BootstrapWitness;

public static class BootstrapWitnessExtensions
{
    public static byte[] PublicKey(this CBootstrapWitness self) => self.PublicKey;

    public static byte[] Signature(this CBootstrapWitness self) => self.Signature;

    public static byte[] ChainCode(this CBootstrapWitness self) => self.ChainCode;

    public static byte[] Attributes(this CBootstrapWitness self) => self.Attributes;
}