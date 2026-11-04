using GapActivator.Application;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;

namespace GapActivator.Architecture.Tests;

public class ViewsTests
{
    private const string QueriesNamespace = "GapActivator.Application.Queries";

    [Fact]
    public void QueryViews_ExposeNoDomainTypeOtherThanIdsAndEnums()
    {
        Type[] publicTypes = [.. ApplicationAssembly.Reference.GetTypes()
            .Where(t => t.Namespace == QueriesNamespace && t.IsPublic)];
        publicTypes.ShouldNotBeEmpty();

        var offenders = publicTypes
            .SelectMany(t => t.GetProperties().Select(p => (Owner: t, Property: p)))
            .SelectMany(x => Flatten(x.Property.PropertyType).Select(type => (x.Owner, x.Property, Type: type)))
            .Where(x => x.Type.Namespace?.StartsWith("GapActivator.Domain", StringComparison.Ordinal) == true)
            .Where(x => !x.Type.IsEnum && x.Type != typeof(ProcessId) && x.Type != typeof(RunId))
            .Select(x => $"{x.Owner.Name}.{x.Property.Name}: {x.Type.Name}")
            .ToList();

        offenders.ShouldBeEmpty("Query views may expose only ids and enums from the domain.");
    }

    // Unwraps Nullable<T>, arrays and generic arguments (IReadOnlyList<T>, ...).
    private static IEnumerable<Type> Flatten(Type type)
    {
        yield return type;

        if (type.HasElementType && type.GetElementType() is { } element)
        {
            foreach (Type inner in Flatten(element))
            {
                yield return inner;
            }
        }

        if (type.IsGenericType)
        {
            foreach (Type argument in type.GetGenericArguments().SelectMany(Flatten))
            {
                yield return argument;
            }
        }
    }
}
