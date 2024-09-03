using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models;

[CborSerializable(CborType.List, IsIndefinite = false)]
public record CborDefiniteList<T>(T[] Value) : ICbor where T : ICbor;

[CborSerializable(CborType.List, IsIndefinite = true)]
public record CborIndefiniteList<T>(T[] Value) : ICbor where T : ICbor;