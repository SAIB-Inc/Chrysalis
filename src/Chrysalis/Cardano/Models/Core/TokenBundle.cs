using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Core;

public record TokenBundle(Dictionary<CborBytes, CborUlong> Value) : CborMap<CborBytes, CborUlong>(Value);