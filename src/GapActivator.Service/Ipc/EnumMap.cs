namespace GapActivator.Service.Ipc;

internal static class EnumMap
{
    /// <summary>Maps an enum member to the member of the same name; a value that is no defined member is rejected.</summary>
    /// <exception cref="IpcInvalidPayloadException">The value is not a defined member, or the target has no member of that name.</exception>
    public static TTarget To<TTarget>(Enum value)
        where TTarget : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(value);

        // Enum.GetName is null for a value outside the enum, so "7" can never be read as a number.
        string? name = Enum.GetName(value.GetType(), value);
        if (name is null || !Enum.TryParse(name, false, out TTarget result) || !Enum.IsDefined(result))
        {
            throw new IpcInvalidPayloadException($"The value is not a defined {typeof(TTarget).Name} member.");
        }

        return result;
    }

    public static TTarget? ToNullable<TTarget>(Enum? value)
        where TTarget : struct, Enum =>
        value is null ? null : To<TTarget>(value);
}
