using System.Text.Json;
using GapActivator.Application.Messaging;
using GapActivator.Domain.Common;
using GapActivator.Ipc;
using GapActivator.Service.Ipc;
using Microsoft.EntityFrameworkCore;

namespace GapActivator.Service.Tests.Ipc;

public sealed class ErrorMapperTests
{
    private const string Inner = "secret detail";

    public static TheoryData<string> Rows =>
    [
        "rejected",
        "concurrency",
        "unknown-message",
        "json",
        "invalid-payload",
        "domain",
        "argument",
        "argument-null",
        "cancelled-stopping",
        "cancelled-running",
        "task-cancelled-stopping",
        "unexpected",
        "invalid-operation",
    ];

    [Theory]
    [MemberData(nameof(Rows))]
    public void Map_EachException_GivesTheTableRow(string row)
    {
        (Exception exception, bool stopping, string code, bool retryable, string message, bool unexpected) = Row(row);

        IpcError error = IpcErrorMapper.Map(exception, "RunNow", stopping);

        error.Code.ShouldBe(code);
        error.Retryable.ShouldBe(retryable);
        error.Message.ShouldBe(message);
        IpcErrorMapper.IsUnexpected(exception, stopping).ShouldBe(unexpected);
        if (code == IpcErrorCodes.Internal)
        {
            error.Message.ShouldNotContain(Inner);
        }
    }

    [Fact]
    public void Map_DomainMessageWithLineBreaksAndBidi_IsOneSanitizedLine()
    {
        IpcError error = IpcErrorMapper.Map(new DomainException("Name 'a\r\nFAKE\u202Eb' is bad."), "RunNow", false);

        error.Message.ShouldBe("Name 'a  FAKE b' is bad.");
    }

    [Fact]
    public void Map_RejectionAndInvalidPayloadMessages_AreSanitizedAndCut()
    {
        string raw = "x\r\n" + new string('y', 500);

        IpcError rejected = IpcErrorMapper.Map(new CommandRejectedException(RejectionCodes.NameTaken, raw), "RunNow", false);
        IpcError invalid = IpcErrorMapper.Map(new IpcInvalidPayloadException(raw), "RunNow", false);

        foreach (IpcError error in new[] { rejected, invalid })
        {
            error.Message.ShouldNotContain("\n");
            error.Message.Length.ShouldBe(200);
        }
    }

    [Fact]
    public void Map_JsonExceptionWithLongMessage_IsCapped()
    {
        IpcError error = IpcErrorMapper.Map(new JsonException(new string('j', 1000)), "RunNow", false);

        error.Code.ShouldBe(IpcErrorCodes.Invalid);
        error.Message.Length.ShouldBeLessThanOrEqualTo("The payload of RunNow is not valid: ".Length + 200);
        error.Message.ShouldEndWith("\u2026");
    }

    private static (Exception Exception, bool Stopping, string Code, bool Retryable, string Message, bool Unexpected) Row(string row) =>
        row switch
        {
            "rejected" => (new CommandRejectedException(RejectionCodes.NameTaken, "A process named 'x' already exists."),
                false, IpcErrorCodes.NameTaken, false, "A process named 'x' already exists.", false),
            "concurrency" => (new DbUpdateConcurrencyException(Inner),
                false, IpcErrorCodes.Conflict, true, "The data changed while the request ran. Try again.", false),
            "unknown-message" => (new IpcUnknownMessageException("Nope"),
                false, IpcErrorCodes.UnknownMessage, false, "Unknown message type 'Nope'.", false),
            "json" => (new JsonException("bad token"),
                false, IpcErrorCodes.Invalid, false, "The payload of RunNow is not valid: bad token", false),
            "invalid-payload" => (new IpcInvalidPayloadException("Not a defined member."),
                false, IpcErrorCodes.Invalid, false, "Not a defined member.", false),
            "domain" => (new DomainException("A name is required."),
                false, IpcErrorCodes.Invalid, false, "A name is required.", false),
            "argument" => (new ArgumentException("Bad argument."),
                false, IpcErrorCodes.Invalid, false, "Bad argument.", false),
            "argument-null" => (new ArgumentNullException(null, "Missing."),
                false, IpcErrorCodes.Invalid, false, "Missing.", false),
            "cancelled-stopping" => (new OperationCanceledException(Inner),
                true, IpcErrorCodes.ServiceStopping, true, "GapActivator is stopping. Try again when it is running.", false),
            "task-cancelled-stopping" => (new TaskCanceledException(Inner),
                true, IpcErrorCodes.ServiceStopping, true, "GapActivator is stopping. Try again when it is running.", false),
            "cancelled-running" => (new OperationCanceledException(Inner),
                false, IpcErrorCodes.Internal, false, "An unexpected error occurred. See the service log.", true),
            "unexpected" => (new InvalidOperationException(Inner),
                false, IpcErrorCodes.Internal, false, "An unexpected error occurred. See the service log.", true),
            "invalid-operation" => (new InvalidOperationException(Inner),
                true, IpcErrorCodes.Internal, false, "An unexpected error occurred. See the service log.", true),
            _ => throw new ArgumentOutOfRangeException(nameof(row)),
        };
}
