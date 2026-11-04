using System.Text.Json.Serialization.Metadata;

namespace GapActivator.Ipc;

/// <summary>One request/response pair of the protocol, with the JSON metadata for both sides.</summary>
public sealed class IpcMessage<TRequest, TResponse>(
    string name,
    JsonTypeInfo<TRequest> request,
    JsonTypeInfo<TResponse> response)
{
    public string Name { get; } = name;

    public JsonTypeInfo<TRequest> RequestType { get; } = request;

    public JsonTypeInfo<TResponse> ResponseType { get; } = response;
}
