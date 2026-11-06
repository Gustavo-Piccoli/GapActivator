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
    [InlineData("abcdef", 4, "abc\u2026")]
    [InlineData("abc", 3, "abc")]
    [InlineData("ab\r\ncd", 4, "ab \u2026")]
    [InlineData("abc", 1, "\u2026")]
    public void SanitizeWithLimit_TruncatesMarksAndCleans(string input, int max, string expected) =>
        NotificationText.Sanitize(input, max).ShouldBe(expected);

    [Fact]
    public void SanitizeWithLimit_LongerThanMax_EndsWithEllipsisAtExactlyMax()
    {
        string result = NotificationText.Sanitize(new string('x', 250), 200);

        result.Length.ShouldBe(200);
        result.ShouldEndWith("\u2026");
    }

    [Fact]
    public void SanitizeWithLimit_ExactlyMax_IsUnchanged()
    {
        string text = new('x', 200);

        NotificationText.Sanitize(text, 200).ShouldBe(text);
    }

    [Fact]
    public void SanitizeWithLimit_HighSurrogateAtTheCut_IsNotSplit()
    {
        // "a" + emoji (2 chars) + "b": keeping max-1 = 2 chars would end on the high surrogate.
        string result = NotificationText.Sanitize("a\U0001F600b", 3);

        result.ShouldBe("a\u2026");
        result.Length.ShouldBeLessThanOrEqualTo(3);
    }
}
