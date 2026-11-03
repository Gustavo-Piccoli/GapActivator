using GapActivator.Domain.Settings;

namespace GapActivator.Application.Ports;

public interface ISecretProtector
{
    ProtectedSecret Protect(string plaintext);

    string Unprotect(ProtectedSecret secret);
}
