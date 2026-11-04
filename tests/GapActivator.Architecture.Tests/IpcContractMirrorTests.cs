using System.Reflection;
using GapActivator.Application;
using GapActivator.Application.Messaging;
using GapActivator.Domain;
using GapActivator.Ipc;

namespace GapActivator.Architecture.Tests;

// Ipc cannot reference Application or Domain, so the wire DTOs are pinned to the types they mirror here.
public class IpcContractMirrorTests
{
    private const string ContractsNamespace = "GapActivator.Ipc.Contracts";

    private static readonly Dictionary<string, string> NonObviousMirrors = new()
    {
        ["RunFilterDto"] = "RunFilter",
        ["HistoryEntryDto"] = "HistoryEntry",
        ["RunOutputDto"] = "RunOutput",
        ["NotificationDto"] = "AppNotification",
    };

    private static Type[] Dtos => [.. IpcAssembly.Reference.GetTypes()
        .Where(t => t.Namespace == ContractsNamespace && t.IsPublic && t.Name.EndsWith("Dto", StringComparison.Ordinal)
                    && t.Name != "CallerDto" && t.Name != "StateChangedDto")];

    [Fact]
    public void EveryDto_MirrorsItsApplicationTypePropertyForProperty()
    {
        Dtos.Length.ShouldBe(23);

        var problems = new List<string>();
        foreach (Type dto in Dtos)
        {
            string stem = NonObviousMirrors.TryGetValue(dto.Name, out string? name) ? name : dto.Name[..^3];
            Type[] candidates = [.. ApplicationAssembly.Reference.GetTypes().Where(t => t.IsPublic && t.Name == stem)];
            if (candidates.Length != 1)
            {
                problems.Add($"{dto.Name}: expected exactly one Application type named {stem}, found {candidates.Length}.");
                continue;
            }

            (string Name, Type Type)[] expected = Parameters(candidates[0]);
            (string Name, Type Type)[] actual = Parameters(dto);
            if (!expected.Select(p => p.Name).SequenceEqual(actual.Select(p => p.Name)))
            {
                problems.Add($"{dto.Name}: members {string.Join(",", actual.Select(p => p.Name))} != {string.Join(",", expected.Select(p => p.Name))}.");
                continue;
            }

            for (int i = 0; i < expected.Length; i++)
            {
                string want = Describe(Map(expected[i].Type));
                string have = Describe(actual[i].Type);
                if (want != have)
                {
                    problems.Add($"{dto.Name}.{actual[i].Name}: {have} != {want}.");
                }
            }
        }

        problems.ShouldBeEmpty();
    }

    [Fact]
    public void EveryWireEnum_HasTheSameMembersAsItsApplicationOrDomainEnum()
    {
        Type[] wireEnums = [.. IpcAssembly.Reference.GetTypes()
            .Where(t => t.Namespace == ContractsNamespace && t.IsEnum && t.Name != "OperatorsGroupMode")];
        wireEnums.Length.ShouldBe(12);

        foreach (Type wire in wireEnums)
        {
            Type[] sources = [.. ApplicationAssembly.Reference.GetTypes().Concat(DomainAssembly.Reference.GetTypes())
                .Where(t => t.IsEnum && t.IsPublic && t.Name == wire.Name)];
            sources.Length.ShouldBe(1, $"{wire.Name}: expected one source enum.");
            Enum.GetNames(wire).ShouldBe(Enum.GetNames(sources[0]), $"{wire.Name} members differ.");
        }

        Enum.GetNames<GapActivator.Ipc.Contracts.OperatorsGroupMode>().ShouldBe(["Enabled", "Disabled"]);
    }

    [Fact]
    public void ErrorCodes_ContainEveryRejectionCodeWithTheSameString()
    {
        string[] rejections = [.. typeof(RejectionCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!)];
        string[] ipc = [.. typeof(IpcErrorCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!)];

        rejections.ShouldNotBeEmpty();
        rejections.Except(ipc).ShouldBeEmpty();
    }

    private static (string Name, Type Type)[] Parameters(Type type) =>
        [.. type.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First().GetParameters()
            .Select(p => (p.Name!, p.ParameterType))];

    // Application/Domain type -> expected wire type.
    private static Type Map(Type type)
    {
        if (Nullable.GetUnderlyingType(type) is { } inner)
        {
            Type mapped = Map(inner);
            return mapped.IsValueType ? typeof(Nullable<>).MakeGenericType(mapped) : mapped;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
        {
            return typeof(IReadOnlyList<>).MakeGenericType(Map(type.GetGenericArguments()[0]));
        }

        if (type.Name is "ProcessId" or "RunId")
        {
            return typeof(Guid);
        }

        if (type.IsEnum)
        {
            return IpcAssembly.Reference.GetType($"{ContractsNamespace}.{type.Name}")
                ?? throw new InvalidOperationException($"No wire enum for {type.Name}.");
        }

        if (type.Namespace?.StartsWith("GapActivator", StringComparison.Ordinal) == true)
        {
            string name = type.Name == "AppNotification" ? "NotificationDto" : type.Name + "Dto";
            return IpcAssembly.Reference.GetType($"{ContractsNamespace}.{name}")
                ?? throw new InvalidOperationException($"No DTO {name} for {type.Name}.");
        }

        return type;
    }

    // Nullable reference annotations are not part of the CLR type, so the text form carries them.
    private static string Describe(Type type) =>
        type.IsGenericType
            ? $"{type.GetGenericTypeDefinition().Name}<{string.Join(",", type.GetGenericArguments().Select(Describe))}>"
            : type.FullName ?? type.Name;
}
