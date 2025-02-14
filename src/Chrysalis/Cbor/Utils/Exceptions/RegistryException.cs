namespace Chrysalis.Cbor.Utils.Exceptions;

public class RegistryException : Exception
{
    public RegistryException(string message) : base(message) { }
    public RegistryException(string message, Exception inner) : base(message, inner) { }
}