using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models;

[CborSerializable(CborType.List)]
public record CborList<T>(T[] Value) : ICbor where T : ICbor;