using System.Xml.Linq;
using GapActivator.Application;
using GapActivator.Application.Messaging;
using GapActivator.Domain;
using GapActivator.Infrastructure;
using GapActivator.Ipc;
using Microsoft.Extensions.DependencyInjection;
using NetArchTest.Rules;

namespace GapActivator.Architecture.Tests;

public class LayeringTests
{
    [Fact]
    public void Domain_DependsOnNoOtherLayer() =>
        Types.InAssembly(DomainAssembly.Reference).ShouldNot()
            .HaveDependencyOnAny("GapActivator.Application", "GapActivator.Infrastructure",
                                 "GapActivator.Service", "GapActivator.Ipc", "GapActivator.App")
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
            .HaveDependencyOnAny("GapActivator.Service", "GapActivator.Ipc", "GapActivator.App")
            .GetResult().IsSuccessful.ShouldBeTrue();

    [Fact]
    public void Infrastructure_PackageReferencesAreTheJustifiedOnes()
    {
        string root = FindSolutionRoot();
        string csproj = Path.Combine(root, "src", "GapActivator.Infrastructure", "GapActivator.Infrastructure.csproj");

        XDocument project = XDocument.Load(csproj);

        string[] packages = [.. project.Descendants("PackageReference").Select(p => (string)p.Attribute("Include")!)];
        packages.Order().ShouldBe(
        [
            "Ical.Net",
            "MailKit",
            "Microsoft.EntityFrameworkCore.Design",
            "Microsoft.EntityFrameworkCore.Sqlite",
            "Microsoft.Extensions.Logging.EventLog",
            "System.Security.Cryptography.Pkcs",
            "System.Security.Cryptography.ProtectedData",
        ]);
    }

    [Fact]
    public void HostingWindowsServices_AcrossAllProjects_IsReferencedOnlyByService()
    {
        string root = FindSolutionRoot();
        string service = Path.Combine(root, "src", "GapActivator.Service", "GapActivator.Service.csproj");
        File.Exists(service).ShouldBeTrue($"Service project not found at '{service}'.");

        string[] referencing = [.. Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(file => XDocument.Load(file).Descendants("PackageReference")
                .Any(p => (string?)p.Attribute("Include") == "Microsoft.Extensions.Hosting.WindowsServices"))
            .Select(file => Path.GetRelativePath(root, file))];

        referencing.ShouldBe([Path.GetRelativePath(root, service)]);
    }

    // Carry-over: ScopedServiceControl is only IAsyncDisposable, so a synchronous scope would never run its disposal.
    [Fact]
    public void ServiceSource_NeverCreatesASynchronousScope()
    {
        string root = FindSolutionRoot();
        string source = Path.Combine(root, "src", "GapActivator.Service");
        Directory.Exists(source).ShouldBeTrue($"Service source folder not found at '{source}'.");

        string[] files = [.. Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))];
        files.ShouldNotBeEmpty($"No Service source files found under '{source}'.");

        string[] offenders = [.. files
            .Where(file => File.ReadAllText(file).Contains(".CreateScope(", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(root, file))];

        offenders.ShouldBeEmpty("Use CreateAsyncScope: a scope that is only IAsyncDisposable is not disposed by Dispose.");
    }

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

    [Fact]
    public void Application_DependsOnlyOnDomain() =>
        Types.InAssembly(ApplicationAssembly.Reference).ShouldNot()
            .HaveDependencyOnAny("GapActivator.Infrastructure", "GapActivator.Service", "GapActivator.Ipc", "GapActivator.App",
                                 "Ical.Net", "Microsoft.EntityFrameworkCore", "System.Diagnostics.Process",
                                 "System.IO.File", "System.IO.Directory", "System.Net.Http",
                                 "Microsoft.Win32", "MailKit")
            .GetResult().IsSuccessful.ShouldBeTrue();

    [Fact]
    public void Application_PackageReferencesAreOnlyTheAllowedAbstractions()
    {
        string root = FindSolutionRoot();
        string csproj = Path.Combine(root, "src", "GapActivator.Application", "GapActivator.Application.csproj");

        XDocument project = XDocument.Load(csproj);

        string[] packages = [.. project.Descendants("PackageReference").Select(p => (string)p.Attribute("Include")!)];
        packages.Order().ShouldBe(
            ["Microsoft.Extensions.DependencyInjection.Abstractions", "Microsoft.Extensions.Logging.Abstractions"]);

        string[] projects = [.. project.Descendants("ProjectReference")
            .Select(p => Path.GetFileName(((string)p.Attribute("Include")!).Replace((char)92, '/')))];
        projects.ShouldBe(["GapActivator.Domain.csproj"]);
    }

    [Fact]
    public void Application_SourceNeverReadsTheSystemClock()
    {
        string root = FindSolutionRoot();
        string source = Path.Combine(root, "src", "GapActivator.Application");
        Directory.Exists(source).ShouldBeTrue($"Application source folder not found at '{source}'.");

        string[] files = [.. Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))];
        files.ShouldNotBeEmpty($"No Application source files found under '{source}'.");

        string[] forbidden =
            ["DateTime.Now", "DateTime.UtcNow", "DateTimeOffset.Now", "DateTimeOffset.UtcNow", "Thread.Sleep", "Stopwatch"];

        var offenders = files
            .SelectMany(file => forbidden
                .Where(token => File.ReadAllText(file).Contains(token, StringComparison.Ordinal))
                .Select(token => $"{Path.GetRelativePath(root, file)}: {token}"))
            .ToList();

        offenders.ShouldBeEmpty("The application must take time from the injected TimeProvider.");
    }

    [Fact]
    public void EveryMessage_HasExactlyOneHandlerRegistered()
    {
        var services = new ServiceCollection().AddApplication();

        var problems = new List<string>();
        foreach (Type type in ApplicationAssembly.Reference.GetTypes().Where(t => t is { IsAbstract: false, IsInterface: false }))
        {
            foreach (Type contract in type.GetInterfaces().Where(i => i.IsGenericType))
            {
                Type definition = contract.GetGenericTypeDefinition();
                Type? handler = null;
                if (definition == typeof(ICommand<>))
                {
                    handler = typeof(ICommandHandler<,>).MakeGenericType(type, contract.GetGenericArguments()[0]);
                }
                else if (definition == typeof(IQuery<>))
                {
                    handler = typeof(IQueryHandler<,>).MakeGenericType(type, contract.GetGenericArguments()[0]);
                }

                if (handler is not null && services.Count(d => d.ServiceType == handler) != 1)
                {
                    problems.Add($"{type.Name}: expected exactly one {handler.Name} registration.");
                }
            }
        }

        problems.ShouldBeEmpty();
    }

    [Fact]
    public void Ipc_DependsOnNoGapActivatorLayer() =>
        Types.InAssembly(IpcAssembly.Reference).ShouldNot()
            .HaveDependencyOnAny("GapActivator.Domain", "GapActivator.Application",
                                 "GapActivator.Infrastructure", "GapActivator.Service")
            .GetResult().IsSuccessful.ShouldBeTrue();

    [Fact]
    public void Ipc_HasNoProjectOrPackageReferences()
    {
        XDocument project = XDocument.Load(IpcCsproj());

        project.Descendants("PackageReference").ShouldBeEmpty();
        project.Descendants("ProjectReference").ShouldBeEmpty();
    }

    [Fact]
    public void Ipc_IsAotCompatibleAndWindowsOnly()
    {
        XDocument project = XDocument.Load(IpcCsproj());

        project.Descendants("IsAotCompatible").Select(e => e.Value).ShouldBe(["true"]);
        project.Descendants("TargetFramework").Select(e => e.Value).ShouldBe(["net10.0-windows"]);
    }

    private static string IpcCsproj() =>
        Path.Combine(FindSolutionRoot(), "src", "GapActivator.Ipc", "GapActivator.Ipc.csproj");

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
