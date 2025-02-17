namespace Chrysalis.Cbor.Utils.Exceptions;

public class CborSerializationException : Exception
{
    public CborSerializationException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

public class CborDeserializationException : Exception
{
    public CborDeserializationException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}