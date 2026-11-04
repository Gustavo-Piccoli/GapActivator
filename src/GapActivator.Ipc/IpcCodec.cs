using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace GapActivator.Ipc;

/// <summary>Serializes and parses envelopes and payloads; every call goes through source-generated metadata.</summary>
public static class IpcCodec
{
    private static readonly byte[] EmptyObject = "{}"u8.ToArray();

    public static byte[] Serialize<T>(T value, JsonTypeInfo<T> type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return JsonSerializer.SerializeToUtf8Bytes(value, type);
    }

    /// <summary>Serializes a payload into the element an envelope carries.</summary>
    public static JsonElement ToElement<T>(T value, JsonTypeInfo<T> type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return JsonSerializer.SerializeToElement(value, type);
    }

    /// <exception cref="IpcProtocolException">Not JSON, an id below 1, or a missing or empty type.</exception>
    public static RequestEnvelope ParseRequest(ReadOnlySpan<byte> frame)
    {
        RequestEnvelope envelope = Parse(frame, IpcJsonContext.Default.RequestEnvelope);
        if (envelope.Id < 1)
        {
            throw new IpcProtocolException("A request needs an id of 1 or more.");
        }

        if (string.IsNullOrEmpty(envelope.Type))
        {
            throw new IpcProtocolException("A request needs a type.");
        }

        return envelope;
    }

    /// <exception cref="IpcProtocolException">Not JSON, or neither an id nor an event.</exception>
    public static ServerEnvelope ParseServer(ReadOnlySpan<byte> frame)
    {
        ServerEnvelope envelope = Parse(frame, IpcJsonContext.Default.ServerEnvelope);
        if (envelope.Id is null && envelope.Event is null)
        {
            throw new IpcProtocolException("A server frame needs an id or an event.");
        }

        return envelope;
    }

    /// <summary>Reads a payload; an absent one is read as <c>{}</c>.</summary>
    /// <exception cref="JsonException">The payload does not fit <typeparamref name="T"/>.</exception>
    public static T ReadPayload<T>(JsonElement? payload, JsonTypeInfo<T> type)
    {
        ArgumentNullException.ThrowIfNull(type);
        T? value = payload is { } element
            ? element.Deserialize(type)
            : JsonSerializer.Deserialize(EmptyObject, type);
        return value ?? throw new JsonException("The payload must not be null.");
    }

    private static T Parse<T>(ReadOnlySpan<byte> frame, JsonTypeInfo<T> type)
        where T : class
    {
        try
        {
            return JsonSerializer.Deserialize(frame, type)
                ?? throw new IpcProtocolException("A frame must be a JSON object.");
        }
        catch (JsonException ex)
        {
            throw new IpcProtocolException($"A frame is not a valid envelope: {ex.Message}");
        }
    }
}
