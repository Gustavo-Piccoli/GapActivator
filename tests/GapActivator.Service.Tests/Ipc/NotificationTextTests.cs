using GapActivator.Service.Ipc;

namespace GapActivator.Service.Tests.Ipc;

public sealed class NotificationTextTests
{
    [Theory]
    [InlineData("a\r\nb", "a  b")]
    [InlineData("x\u001b[31m", "x [31m")]
    [InlineData("\u202Eevil", " evil")]
    [InlineData("a\u2028b\u2029c", "a b c")]
    [InlineData("a\u200Eb\u200Fc\u2066d\u2069e", "a b c d e")]
    [InlineData("ok ç 密", "ok ç 密")]
    [InlineData("", "")]
    public void Sanitize_ControlAndBidiCharacters_BecomeSpaces(string input, string expected) =>
        NotificationText.Sanitize(input).ShouldBe(expected);
}
