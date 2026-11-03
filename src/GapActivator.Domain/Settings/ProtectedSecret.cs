using GapActivator.Domain.Common;

namespace GapActivator.Domain.Settings;

/// <summary>Opaque, already-encrypted secret. Never prints its content.</summary>
public sealed record ProtectedSecret
{
    public ProtectedSecret(string ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext))
        {
            throw new DomainException("Protected secret must not be empty.");
        }

        Ciphertext = ciphertext;
    }

    public string Ciphertext { get; }

    public override string ToString() => "***";
}
