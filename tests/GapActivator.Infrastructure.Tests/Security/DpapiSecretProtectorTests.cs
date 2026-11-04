using System.Security.Cryptography;
using GapActivator.Domain.Settings;
using GapActivator.Infrastructure.Security;

namespace GapActivator.Infrastructure.Tests.Security;

public sealed class DpapiSecretProtectorTests
{
    private readonly DpapiSecretProtector _protector = new();

    [Theory]
    [InlineData("")]
    [InlineData("p@ss")]
    [InlineData("senha-çãõ-密码")]
    public void Protect_ThenUnprotect_RoundTrips(string plaintext) =>
        _protector.Unprotect(_protector.Protect(plaintext)).ShouldBe(plaintext);

    [Fact]
    public void Protect_SamePlaintextTwice_GivesDifferentCiphertextsWithPrefix()
    {
        ProtectedSecret first = _protector.Protect("p@ss");
        ProtectedSecret second = _protector.Protect("p@ss");

        first.Ciphertext.ShouldStartWith("dpapi:v1:");
        second.Ciphertext.ShouldStartWith("dpapi:v1:");
        first.Ciphertext.ShouldNotBe(second.Ciphertext);
        first.Ciphertext.ShouldNotContain("p@ss");
    }

    [Fact]
    public void Unprotect_TamperedCiphertext_ThrowsCryptographicException()
    {
        string ciphertext = _protector.Protect("p@ss").Ciphertext;
        byte[] bytes = Convert.FromBase64String(ciphertext["dpapi:v1:".Length..]);
        bytes[^1] ^= 0xFF;
        var tampered = new ProtectedSecret("dpapi:v1:" + Convert.ToBase64String(bytes));

        Should.Throw<CryptographicException>(() => _protector.Unprotect(tampered));
    }

    [Fact]
    public void Unprotect_ForeignFormat_ThrowsCryptographicException() =>
        Should.Throw<CryptographicException>(() => _protector.Unprotect(new ProtectedSecret("enc:x")))
            .Message.ShouldBe("Unknown secret format.");

    [Fact]
    public void Unprotect_PrefixWithoutValidBase64_ThrowsCryptographicException() =>
        Should.Throw<CryptographicException>(() => _protector.Unprotect(new ProtectedSecret("dpapi:v1:***")));
}
