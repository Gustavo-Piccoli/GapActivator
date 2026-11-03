namespace GapActivator.Domain.Common;

/// <summary>The only exception type the domain throws for rule violations.</summary>
public sealed class DomainException(string message) : Exception(message);
