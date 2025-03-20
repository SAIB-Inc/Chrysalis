using CBootStrapWitness = Chrysalis.Cbor.Cardano.Types.Block.Transaction.WitnessSet.BootstrapWitness;

namespace Chrysalis.Cbor.Extensions.Block.Transaction.WitnessSet.BootstrapWitness;

public static class BootstrapWitnessExtensions
{
    public static byte[] PublicKey(this CBootStrapWitness self) => self.PublicKey;

    public static byte[] Signature(this CBootStrapWitness self) => self.Signature;

    public static byte[] ChainCode(this CBootStrapWitness self) => self.ChainCode;

    public static byte[] Attributes(this CBootStrapWitness self) => self.Attributes;
}