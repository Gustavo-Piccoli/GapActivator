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
