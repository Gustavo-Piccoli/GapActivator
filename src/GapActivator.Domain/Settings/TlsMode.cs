namespace GapActivator.Domain.Settings;

/// <summary>Transport security used when connecting to the SMTP server.</summary>
public enum TlsMode
{
    None,
    StartTls,
    Implicit,
}
