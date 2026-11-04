using System.Security.Cryptography;
using System.Text;
using GapActivator.Application.Ports;
using GapActivator.Domain.Settings;

namespace GapActivator.Infrastructure.Security;

/// <summary>Protects secrets with machine-scope DPAPI, so any account on this machine can read them.</summary>
internal sealed class DpapiSecretProtector : ISecretProtector
{
    private const string Prefix = "dpapi:v1:";

    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("GapActivator.Secret.v1");

    public ProtectedSecret Protect(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        byte[] cipher = ProtectedData.Protect(Encoding.UTF8.GetBytes(plaintext), Entropy, DataProtectionScope.LocalMachine);
        return new ProtectedSecret(Prefix + Convert.ToBase64String(cipher));
    }

    public string Unprotect(ProtectedSecret secret)
    {
        ArgumentNullException.ThrowIfNull(secret);

        if (!secret.Ciphertext.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new CryptographicException("Unknown secret format.");
        }

        byte[] cipher;
        try
        {
            cipher = Convert.FromBase64String(secret.Ciphertext[Prefix.Length..]);
        }
        catch (FormatException ex)
        {
            throw new CryptographicException("Unknown secret format.", ex);
        }

        return Encoding.UTF8.GetString(ProtectedData.Unprotect(cipher, Entropy, DataProtectionScope.LocalMachine));
    }
}
