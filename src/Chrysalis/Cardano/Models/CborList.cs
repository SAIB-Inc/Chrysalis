using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models;

[CborSerializable(CborType.List, IsIndefinite = true)]
public record CborList<T>(T[] Value) : ICbor where T : ICbor;