using System.Text;
using System.Text.Json;
using GapActivator.Ipc.Contracts;

namespace GapActivator.Ipc.Tests;

public class EnvelopeTests
{
    private static string Json(byte[] bytes) => Encoding.UTF8.GetString(bytes);

    private static JsonElement Element(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void Request_Serializes_ExactlyTheProtocolShape()
    {
        var request = new RequestEnvelope
        {
            Id = 7,
            Type = "RunNow",
            Payload = Element("{\"id\":\"00000000-0000-0000-0000-000000000001\"}"),
        };

        Json(IpcCodec.Serialize(request, IpcJsonContext.Default.RequestEnvelope)).ShouldBe(
            "{\"id\":7,\"type\":\"RunNow\",\"payload\":{\"id\":\"00000000-0000-0000-0000-000000000001\"}}");
    }

    // Pre-flight C15: an absent payload is omitted, never written as null.
    [Fact]
    public void Request_WithoutPayload_OmitsThePayloadMember()
    {
        var request = new RequestEnvelope { Id = 1, Type = "PauseInstallation" };

        Json(IpcCodec.Serialize(request, IpcJsonContext.Default.RequestEnvelope)).ShouldBe(
            "{\"id\":1,\"type\":\"PauseInstallation\"}");
    }

    // Pre-flight C6: Event is serialized first.
    [Fact]
    public void Response_OkAndFailure_SerializeExactlyTheProtocolShapes()
    {
        ServerEnvelope ok = ServerEnvelope.Response(7, Element("{}"));
        ServerEnvelope failure = ServerEnvelope.Failure(7, new IpcError("not-found", "m", false));

        Json(IpcCodec.Serialize(ok, IpcJsonContext.Default.ServerEnvelope))
            .ShouldBe("{\"id\":7,\"ok\":true,\"payload\":{}}");
        Json(IpcCodec.Serialize(failure, IpcJsonContext.Default.ServerEnvelope)).ShouldBe(
            "{\"id\":7,\"ok\":false,\"error\":{\"code\":\"not-found\",\"message\":\"m\",\"retryable\":false}}");
    }

    [Fact]
    public void Push_SerializesEventAndPayloadWithoutId()
    {
        JsonElement payload = IpcCodec.ToElement(new StateChangedDto(null, null), IpcJsonContext.Default.StateChangedDto);
        ServerEnvelope push = ServerEnvelope.Push(IpcEvents.StateChanged, payload);

        Json(IpcCodec.Serialize(push, IpcJsonContext.Default.ServerEnvelope)).ShouldBe(
            "{\"event\":\"StateChanged\",\"payload\":{\"processId\":null,\"runId\":null}}");
    }

    [Fact]
    public void ParseServer_ResponseAndPush_AreRecognised()
    {
        ServerEnvelope response = IpcCodec.ParseServer("{\"id\":3,\"ok\":true,\"payload\":{}}"u8);
        ServerEnvelope push = IpcCodec.ParseServer("{\"event\":\"Notification\",\"payload\":{}}"u8);

        response.Id.ShouldBe(3);
        response.Ok.ShouldBe(true);
        push.Id.ShouldBeNull();
        push.Event.ShouldBe("Notification");
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"ok\":true}")]
    [InlineData("null")]
    public void ParseServer_MalformedEnvelope_ThrowsProtocolException(string frame) =>
        Should.Throw<IpcProtocolException>(() => IpcCodec.ParseServer(Encoding.UTF8.GetBytes(frame)));

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"type\":\"Hello\"}")]
    [InlineData("{\"id\":0,\"type\":\"Hello\"}")]
    [InlineData("{\"id\":1}")]
    [InlineData("{\"id\":1,\"type\":\"\"}")]
    public void ParseRequest_MalformedEnvelope_ThrowsProtocolException(string frame) =>
        Should.Throw<IpcProtocolException>(() => IpcCodec.ParseRequest(Encoding.UTF8.GetBytes(frame)));

    [Fact]
    public void ParseRequest_WithoutPayload_Succeeds()
    {
        RequestEnvelope request = IpcCodec.ParseRequest("{\"id\":1,\"type\":\"PauseInstallation\"}"u8);

        request.Id.ShouldBe(1);
        request.Type.ShouldBe("PauseInstallation");
        request.Payload.ShouldBeNull();
    }

    [Fact]
    public void ReadPayload_MissingRequiredMember_ThrowsJsonException() =>
        Should.Throw<JsonException>(() =>
            IpcCodec.ReadPayload(Element("{}"), IpcJsonContext.Default.RunNowRequest));

    [Fact]
    public void ReadPayload_NullForNonNullableString_ThrowsJsonException() =>
        Should.Throw<JsonException>(() => IpcCodec.ReadPayload(
            Element("{\"rule\":null,\"anchor\":\"2030-01-01T00:00:00+00:00\",\"count\":3}"),
            IpcJsonContext.Default.PreviewScheduleRequest));

    [Theory]
    [InlineData("0")]
    [InlineData("\"Bogus\"")]
    public void ReadPayload_EnumAsNumberOrUnknownName_ThrowsJsonException(string outcome) =>
        Should.Throw<JsonException>(() => IpcCodec.ReadPayload(
            Element("{\"filter\":{\"processId\":null,\"outcomes\":[" + outcome + "],\"from\":null,\"to\":null,\"page\":1,\"pageSize\":10}}"),
            IpcJsonContext.Default.ListRunsRequest));

    [Fact]
    public void ReadPayload_EnumByName_Succeeds()
    {
        ListRunsRequest request = IpcCodec.ReadPayload(
            Element("{\"filter\":{\"processId\":null,\"outcomes\":[\"Failed\"],\"from\":null,\"to\":null,\"page\":1,\"pageSize\":10}}"),
            IpcJsonContext.Default.ListRunsRequest);

        request.Filter.Outcomes.ShouldBe([RunOutcome.Failed]);
    }

    [Fact]
    public void ReadPayload_AbsentPayloadForParameterlessRequest_Succeeds()
    {
        PauseInstallationRequest request = IpcCodec.ReadPayload(null, IpcJsonContext.Default.PauseInstallationRequest);

        request.ShouldNotBeNull();
    }

    [Fact]
    public void Dto_DateTimeOffsetWithSevenDigitsAndOffset_RoundTripsToTheTick()
    {
        var at = new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.FromHours(3)).AddTicks(1234567);
        var dto = new ScheduleInputDto(null, ScheduleInputKind.OneTime, at, null);

        byte[] bytes = IpcCodec.Serialize(dto, IpcJsonContext.Default.ScheduleInputDto);
        Json(bytes).ShouldContain("2030-01-01T12:00:00.1234567+03:00");
        ScheduleInputDto back = IpcCodec.ReadPayload(
            JsonDocument.Parse(bytes).RootElement.Clone(), IpcJsonContext.Default.ScheduleInputDto);

        back.At.UtcTicks.ShouldBe(at.UtcTicks);
        back.At.Offset.ShouldBe(at.Offset);
    }

    [Fact]
    public void Dto_EnumsAreWrittenAsTheirNames()
    {
        var definition = new ProcessDefinitionDto(
            "n", "d", ProcessKind.RunToCompletion, "e.exe", string.Empty, null, [], null, null, []);

        Json(IpcCodec.Serialize(definition, IpcJsonContext.Default.ProcessDefinitionDto))
            .ShouldContain("\"kind\":\"RunToCompletion\"");
    }

    [Fact]
    public void Dto_NullMembers_AreWrittenAsNull()
    {
        var dto = new RunFilterDto(null, null, null, null, 1, 10);

        Json(IpcCodec.Serialize(dto, IpcJsonContext.Default.RunFilterDto)).ShouldBe(
            "{\"processId\":null,\"outcomes\":null,\"from\":null,\"to\":null,\"page\":1,\"pageSize\":10}");
    }
}
