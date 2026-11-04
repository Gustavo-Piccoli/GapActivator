using GapActivator.Service.Ipc;

namespace GapActivator.Service.Tests.Ipc;

public sealed class NotificationTextTests
{
    [Theory]
    [InlineData("a\r\nb", "a  b")]
    [InlineData("x\u001b[31m", "x [31m")]
    [InlineData("\u202Eevil", " evil")]
    [InlineData("a\u2028b\u2029c", "a b c")]
    [InlineData("a\u061Cb", "a b")]
    [InlineData("a\u200Eb\u200Fc\u2066d\u2069e", "a b c d e")]
    [InlineData("ok ç 密", "ok ç 密")]
    [InlineData("", "")]
    public void Sanitize_ControlAndBidiCharacters_BecomeSpaces(string input, string expected) =>
        NotificationText.Sanitize(input).ShouldBe(expected);

    [Theory]
    [InlineData("abcdef", 3, "abc")]
    [InlineData("abc", 3, "abc")]
    [InlineData("ab\r\ncd", 4, "ab  ")]
    [InlineData("a\U0001F600b", 2, "a")]
    public void SanitizeWithLimit_TruncatesThenCleans(string input, int max, string expected) =>
        NotificationText.Sanitize(input, max).ShouldBe(expected);
}
