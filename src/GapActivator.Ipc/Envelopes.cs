using System.Text.Json;
using System.Text.Json.Serialization;

namespace GapActivator.Ipc;

public sealed record IpcError(string Code, string Message, bool Retryable);

/// <summary>A request: <c>{"id":n,"type":"Name","payload":{...}}</c>; the payload is absent for a parameterless message.</summary>
public sealed class RequestEnvelope
{
    public int Id { get; set; }

    public string? Type { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Payload { get; set; }
}

/// <summary>A response (<c>id</c> and <c>ok</c>) or a push (<c>event</c>); absent members are left out.</summary>
public sealed class ServerEnvelope
{
    // Declared first so that a push serializes as {"event":...,"payload":...}.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Event { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Id { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Ok { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Payload { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IpcError? Error { get; set; }

    public static ServerEnvelope Response(int id, JsonElement payload) =>
        new() { Id = id, Ok = true, Payload = payload };

    public static ServerEnvelope Failure(int id, IpcError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new ServerEnvelope { Id = id, Ok = false, Error = error };
    }

    public static ServerEnvelope Push(string eventName, JsonElement payload) =>
        new() { Event = eventName, Payload = payload };
}
