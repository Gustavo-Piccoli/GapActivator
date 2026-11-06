using System.ComponentModel;
using System.Security.Principal;
using GapActivator.Infrastructure;

namespace GapActivator.Service.Setup;

/// <summary>The operations the setup verbs run. The seam for tests; <see cref="Default"/> is the real implementation.</summary>
internal sealed record SetupActions(
    Func<string, SecurityIdentifier> ResolveAccount,
    Action<string, SecurityIdentifier> PrepareDataRoot,
    Action<SecurityIdentifier> GrantLogonRight,
    Action RegisterEventSource,
    Action UnregisterEventSource)
{
    public static SetupActions Default { get; } = new(
        account => (SecurityIdentifier)new NTAccount(account).Translate(typeof(SecurityIdentifier)),
        (path, sid) => DataRootPreparer.Prepare(path, sid),
        ServiceLogonRight.Grant,
        () => new EventSourceRegistrar().Register(),
        () => new EventSourceRegistrar().Unregister());
}

/// <summary>
/// The <c>setup</c> verb, run by the installer before any host or logger exists. It writes only to standard output and
/// standard error. Exit codes: 0 success, 2 usage error, 1 failure (one line on standard error). No verb reads a password.
/// </summary>
internal static class SetupCommand
{
    public const int Success = 0;
    public const int Failure = 1;
    public const int Usage = 2;

    private const string Usage1 =
        "usage: setup prepare-data-root --account <account> [--path <dir>] | grant-logon-right --account <account> | "
        + "register-event-source | unregister-event-source";

    /// <summary>True when the first argument is exactly <c>setup</c>.</summary>
    public static bool IsSetup(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return args.Length > 0 && args[0] == "setup";
    }

    /// <param name="args">The arguments after <c>setup</c>.</param>
    public static Task<int> RunAsync(string[] args, TextWriter stdout, TextWriter stderr, SetupActions? actions = null)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);
        return Task.FromResult(Run(args, stdout, stderr, actions ?? SetupActions.Default));
    }

    private static int Run(string[] args, TextWriter stdout, TextWriter stderr, SetupActions actions)
    {
        if (args.Length == 0)
        {
            return UsageError(stderr, Usage1);
        }

        string verb = args[0];
        Dictionary<string, string>? options = ParseOptions(args.AsSpan(1));
        if (options is null)
        {
            return UsageError(stderr, "invalid arguments for '" + verb + "'. " + Usage1);
        }

        switch (verb)
        {
            case "prepare-data-root":
                {
                    if (!Accepts(options, ["--account", "--path"]) || !options.TryGetValue("--account", out string? account))
                    {
                        return UsageError(stderr, "prepare-data-root needs --account <account> and takes an optional --path <dir>.");
                    }

                    string path;
                    try
                    {
                        path = options.TryGetValue("--path", out string? given)
                            ? new GapActivatorPaths(given).DataRoot
                            : GapActivatorPaths.Default.DataRoot;
                    }
                    catch (ArgumentException)
                    {
                        return UsageError(stderr, "--path must be a fully qualified folder.");
                    }

                    return Execute(stdout, stderr, "data root prepared: " + path, () =>
                        actions.PrepareDataRoot(path, Resolve(actions, account)));
                }

            case "grant-logon-right":
                {
                    if (!Accepts(options, ["--account"]) || !options.TryGetValue("--account", out string? account))
                    {
                        return UsageError(stderr, "grant-logon-right needs --account <account>.");
                    }

                    return Execute(stdout, stderr, "logon right granted to " + account, () =>
                        actions.GrantLogonRight(Resolve(actions, account)));
                }

            case "register-event-source":
            case "unregister-event-source":
                {
                    if (options.Count != 0)
                    {
                        return UsageError(stderr, verb + " takes no arguments.");
                    }

                    return verb == "register-event-source"
                        ? Execute(stdout, stderr, "event source registered", actions.RegisterEventSource)
                        : Execute(stdout, stderr, "event source unregistered", actions.UnregisterEventSource);
                }

            default:
                return UsageError(stderr, "unknown setup verb '" + verb + "'. " + Usage1);
        }
    }

    private static SecurityIdentifier Resolve(SetupActions actions, string account)
    {
        try
        {
            return actions.ResolveAccount(account);
        }
        catch (Exception ex) when (ex is IdentityNotMappedException or ArgumentException or SystemException)
        {
            throw new SetupFailedException($"cannot resolve the account '{account}'.", ex);
        }
    }

    private static int Execute(TextWriter stdout, TextWriter stderr, string successMessage, Action action)
    {
        try
        {
            action();
        }
        catch (SetupFailedException ex)
        {
            return Fail(stderr, ex.Message);
        }
        catch (Win32Exception ex)
        {
            return Fail(stderr, $"Win32 error {ex.NativeErrorCode}: {ex.Message}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
            or System.Security.SecurityException)
        {
            int win32 = ex is IOException io ? io.HResult & 0xFFFF : ex is UnauthorizedAccessException ? 5 : 0;
            return Fail(stderr, win32 == 0 ? ex.Message : $"Win32 error {win32}: {ex.Message}");
        }

        stdout.WriteLine(successMessage);
        return Success;
    }

    private static int Fail(TextWriter stderr, string message)
    {
        stderr.WriteLine("setup failed: " + OneLine(message));
        return Failure;
    }

    private static int UsageError(TextWriter stderr, string message)
    {
        stderr.WriteLine("setup: " + OneLine(message));
        return Usage;
    }

    private static string OneLine(string text) => text.ReplaceLineEndings(" ").Trim();

    private static bool Accepts(Dictionary<string, string> options, string[] allowed) =>
        options.Keys.All(k => allowed.Contains(k));

    /// <summary>Parses <c>--name value</c> pairs; null when an argument is not an option, lacks a value or repeats.</summary>
    private static Dictionary<string, string>? ParseOptions(ReadOnlySpan<string> args)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i += 2)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Length
                || !options.TryAdd(args[i], args[i + 1]))
            {
                return null;
            }
        }

        return options;
    }

    private sealed class SetupFailedException(string message, Exception inner) : Exception(message, inner);
}
