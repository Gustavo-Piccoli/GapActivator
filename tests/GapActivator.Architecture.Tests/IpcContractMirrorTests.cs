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
        var context = new NullabilityInfoContext();
        foreach (Type dto in Dtos)
        {
            string stem = NonObviousMirrors.TryGetValue(dto.Name, out string? name) ? name : dto.Name[..^3];
            Type[] candidates = [.. ApplicationAssembly.Reference.GetTypes().Where(t => t.IsPublic && t.Name == stem)];
            if (candidates.Length != 1)
            {
                problems.Add($"{dto.Name}: expected exactly one Application type named {stem}, found {candidates.Length}.");
                continue;
            }

            ParameterInfo[] expectedInfo = ParameterInfos(candidates[0]);
            ParameterInfo[] actualInfo = ParameterInfos(dto);
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

                string wantNull = NullabilityShape(context.Create(expectedInfo[i]));
                string haveNull = NullabilityShape(context.Create(actualInfo[i]));
                if (wantNull != haveNull)
                {
                    problems.Add($"{dto.Name}.{actual[i].Name}: nullability {haveNull} != {wantNull}.");
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

    private static ParameterInfo[] ParameterInfos(Type type) =>
        type.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First().GetParameters();

    private static (string Name, Type Type)[] Parameters(Type type) =>
        [.. ParameterInfos(type).Select(p => (p.Name!, p.ParameterType))];

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

    // The CLR type string; reference nullability is compared separately through NullabilityShape.
    private static string Describe(Type type) =>
        type.IsGenericType
            ? $"{type.GetGenericTypeDefinition().Name}<{string.Join(",", type.GetGenericArguments().Select(Describe))}>"
            : type.FullName ?? type.Name;

    // Nullability is part of the wire contract (RespectNullableAnnotations): '?' marks a nullable reference or
    // Nullable<T>, recursing into generic arguments such as list elements. Nested DTOs are checked as their own pair.
    private static string NullabilityShape(NullabilityInfo info)
    {
        string mark = Nullable.GetUnderlyingType(info.Type) is not null || info.ReadState == NullabilityState.Nullable
            ? "?"
            : string.Empty;
        return info.GenericTypeArguments.Length == 0
            ? mark
            : $"{mark}<{string.Join(",", info.GenericTypeArguments.Select(NullabilityShape))}>";
    }
}
