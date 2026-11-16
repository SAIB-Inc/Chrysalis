using Chrysalis.Tx.Providers.Lean;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Grpc.Core;
using Utxorpc.V1Beta.Query;
using Utxorpc.V1Beta.Submit;

namespace Chrysalis.Tx.Providers;

/// <summary>The version-specific gRPC method descriptors and message marshallers.</summary>
internal sealed class UtxoRpcMethods
{
    internal static readonly UtxoRpcMethods V1Beta = new();

    private UtxoRpcMethods()
    {
        ServiceDescriptor query = QueryReflection.Descriptor.FindTypeByName<ServiceDescriptor>("QueryService");
        ServiceDescriptor submit = SubmitReflection.Descriptor.FindTypeByName<ServiceDescriptor>("SubmitService");
        ReadUtxos = Create<ReadUtxosRequest, LeanUtxosResponse>(query, "ReadUtxos", LeanUtxosResponse.Parser.WithDiscardUnknownFields(true));
        SearchUtxos = Create<SearchUtxosRequest, LeanUtxosResponse>(query, "SearchUtxos", LeanUtxosResponse.Parser.WithDiscardUnknownFields(true));
        ReadTx = Create<ReadTxRequest, LeanReadTxResponse>(query, "ReadTx", LeanReadTxResponse.Parser.WithDiscardUnknownFields(true));
        SubmitTx = Create<SubmitTxRequest, SubmitTxResponse>(submit, "SubmitTx", SubmitTxResponse.Parser);
        ReadParams = Create<ReadParamsRequest, ReadParamsResponse>(query, "ReadParams", ReadParamsResponse.Parser);
        WaitForTx = Create<WaitForTxRequest, WaitForTxResponse>(submit, "WaitForTx", WaitForTxResponse.Parser);
    }

    internal Method<ReadParamsRequest, ReadParamsResponse> ReadParams { get; }

    internal Method<ReadUtxosRequest, LeanUtxosResponse> ReadUtxos { get; }
    internal Method<SearchUtxosRequest, LeanUtxosResponse> SearchUtxos { get; }
    internal Method<ReadTxRequest, LeanReadTxResponse> ReadTx { get; }
    internal Method<SubmitTxRequest, SubmitTxResponse> SubmitTx { get; }
    internal Method<WaitForTxRequest, WaitForTxResponse> WaitForTx { get; }

    private static Method<TRequest, TResponse> Create<TRequest, TResponse>(ServiceDescriptor service, string name, MessageParser<TResponse> parser)
        where TRequest : class, IMessage<TRequest>
        where TResponse : class, IMessage<TResponse>
    {
        MethodDescriptor method = service.FindMethodByName(name)
            ?? throw new InvalidOperationException($"{service.FullName} has no method {name}.");
        return new Method<TRequest, TResponse>(
            method.IsServerStreaming ? MethodType.ServerStreaming : MethodType.Unary,
            service.FullName,
            method.Name,
            Marshallers.Create<TRequest>(Write, static _ => throw new NotSupportedException()),
            Marshallers.Create(Write, context => parser.ParseFrom(context.PayloadAsReadOnlySequence())));
    }

    private static void Write<T>(T message, SerializationContext context)
        where T : IMessage<T>
    {
        context.SetPayloadLength(message.CalculateSize());
        message.WriteTo(context.GetBufferWriter());
        context.Complete();
    }
}
