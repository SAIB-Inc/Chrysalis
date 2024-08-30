using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models;

public record TokenBundle(Dictionary<CborBytes, CborUlong> Value) : CborMap<CborBytes, CborUlong>(Value);