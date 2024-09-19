using Chrysalis.Cardano.Models.Cbor;

namespace Chrysalis.Cardano.Models.Core.Transaction;

public record TokenBundle(Dictionary<CborBytes, CborUlong> Value) : CborMap<CborBytes, CborUlong>(Value);