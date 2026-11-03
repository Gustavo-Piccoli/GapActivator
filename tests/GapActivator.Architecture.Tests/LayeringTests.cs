using System.Xml.Linq;
using GapActivator.Domain;
using GapActivator.Infrastructure;
using NetArchTest.Rules;

namespace GapActivator.Architecture.Tests;

public class LayeringTests
{
    [Fact]
    public void Domain_DependsOnNoOtherLayer() =>
        Types.InAssembly(DomainAssembly.Reference).ShouldNot()
            .HaveDependencyOnAny("GapActivator.Application", "GapActivator.Infrastructure",
                                 "GapActivator.Service", "GapActivator.App")
            .GetResult().IsSuccessful.ShouldBeTrue();

    [Fact]
    public void Domain_DependsOnNoFrameworkOrIo() =>
        Types.InAssembly(DomainAssembly.Reference).ShouldNot()
            .HaveDependencyOnAny("Ical.Net", "Microsoft.EntityFrameworkCore", "Microsoft.Extensions",
                                 "System.IO.File", "System.IO.Directory", "System.IO.FileStream",
                                 "System.Net.Http", "System.Text.Json", "Microsoft.Win32",
                                 "System.Diagnostics.Process", "System.Runtime.InteropServices.Marshal")
            .GetResult().IsSuccessful.ShouldBeTrue();

    [Fact]
    public void Infrastructure_DoesNotDependOnOuterLayers() =>
        Types.InAssembly(InfrastructureAssembly.Reference).ShouldNot()
            .HaveDependencyOnAny("GapActivator.Service", "GapActivator.App")
            .GetResult().IsSuccessful.ShouldBeTrue();

    [Fact]
    public void Domain_HasNoPackageOrProjectReferences()
    {
        string root = FindSolutionRoot();
        string csproj = Path.Combine(root, "src", "GapActivator.Domain", "GapActivator.Domain.csproj");

        XDocument project = XDocument.Load(csproj);

        project.Descendants("PackageReference").ShouldBeEmpty();
        project.Descendants("ProjectReference").ShouldBeEmpty();
    }

    [Fact]
    public void Domain_SourceNeverReadsAClock()
    {
        string root = FindSolutionRoot();
        string domainSource = Path.Combine(root, "src", "GapActivator.Domain");
        Directory.Exists(domainSource).ShouldBeTrue($"Domain source folder not found at '{domainSource}'.");

        string[] files = [.. Directory.EnumerateFiles(domainSource, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))];
        files.ShouldNotBeEmpty($"No Domain source files found under '{domainSource}'.");

        string[] forbidden =
            ["DateTime.Now", "DateTime.UtcNow", "DateTimeOffset.Now", "DateTimeOffset.UtcNow", "TimeProvider", "Stopwatch"];

        var offenders = files
            .SelectMany(file => forbidden
                .Where(token => File.ReadAllText(file).Contains(token, StringComparison.Ordinal))
                .Select(token => $"{Path.GetRelativePath(root, file)}: {token}"))
            .ToList();

        offenders.ShouldBeEmpty("The domain must receive 'now' from callers and never read a clock.");
    }

    private static string FindSolutionRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GapActivator.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("GapActivator.slnx not found.");
    }
}
