using System.Reflection;
using Dms.SharedKernel;
using NetArchTest.Rules;
using Shouldly;

namespace Dms.ArchitectureTests;

/// <summary>
/// The modular monolith only stays modular if the boundaries are checked. These tests fail the
/// build when a module reaches past a contract or when core code picks up an infrastructure
/// dependency.
/// </summary>
public sealed class ModuleBoundaryTests
{
    private static readonly Assembly IdentityCore = typeof(Identity.Domain.User).Assembly;
    private static readonly Assembly AuthorizationCore = typeof(Authorization.Domain.Role).Assembly;
    private static readonly Assembly AuditCore = typeof(Audit.Domain.AuditEntry).Assembly;
    private static readonly Assembly DocumentsCore = typeof(Documents.Domain.Document).Assembly;
    private static readonly Assembly DocumentTypesCore = typeof(DocumentTypes.Domain.DocumentType).Assembly;
    private static readonly Assembly StorageCore = typeof(Storage.Domain.StorageObject).Assembly;
    private static readonly Assembly WorkflowCore = typeof(Workflow.Domain.WorkflowInstance).Assembly;
    private static readonly Assembly SharedKernel = typeof(Entity<>).Assembly;

    /// <summary>Every module, so a new one cannot quietly skip the boundary rules.</summary>
    private static readonly string[] Modules = ["Identity", "Authorization", "Audit", "Documents", "DocumentTypes", "Storage", "Workflow"];

    public static TheoryData<string, Assembly> CoreAssemblies => new()
    {
        { "Identity", IdentityCore },
        { "Authorization", AuthorizationCore },
        { "Audit", AuditCore },
        { "Documents", DocumentsCore },
        { "DocumentTypes", DocumentTypesCore },
        { "Storage", StorageCore },
        { "Workflow", WorkflowCore },
    };

    [Theory]
    [MemberData(nameof(CoreAssemblies))]
    public void Modules_reach_each_other_only_through_contracts(string module, Assembly assembly)
    {
        // Namespaces of other modules, except their .Contracts. "Dms.Documents" must not match
        // "Dms.DocumentTypes", hence the trailing dot.
        var forbidden = Modules
            .Where(other => other != module)
            .SelectMany(other => new[]
            {
                $"Dms.{other}.Domain",
                $"Dms.{other}.Application",
                $"Dms.{other}.Infrastructure",
            })
            .ToArray();

        var result = Types.InAssembly(assembly)
            .ShouldNot()
            .HaveDependencyOnAny(forbidden)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue($"{module}: {Describe(result)}");
    }

    [Theory]
    [MemberData(nameof(CoreAssemblies))]
    public void Module_core_does_not_depend_on_persistence_or_the_web_stack(string module, Assembly assembly)
    {
        var result = Types.InAssembly(assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Microsoft.EntityFrameworkCore",
                "Npgsql",
                "Microsoft.AspNetCore")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            $"{module} core must stay free of infrastructure: {Describe(result)}");
    }

    [Fact]
    public void The_shared_kernel_depends_on_nothing_of_ours()
    {
        var result = Types.InAssembly(SharedKernel)
            .ShouldNot()
            .HaveDependencyOnAny("Dms.Application", "Dms.Infrastructure", "Dms.Identity", "Dms.Authorization", "Dms.Audit")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Fact]
    public void Identity_reaches_other_modules_only_through_their_contracts()
    {
        var result = Types.InAssembly(IdentityCore)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Dms.Authorization.Domain",
                "Dms.Authorization.Application",
                "Dms.Authorization.Infrastructure",
                "Dms.Audit.Domain",
                "Dms.Audit.Application",
                "Dms.Audit.Infrastructure")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Fact]
    public void Authorization_reaches_other_modules_only_through_their_contracts()
    {
        var result = Types.InAssembly(AuthorizationCore)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Dms.Identity.Domain",
                "Dms.Identity.Application",
                "Dms.Identity.Infrastructure",
                "Dms.Audit.Domain",
                "Dms.Audit.Application",
                "Dms.Audit.Infrastructure")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Fact]
    public void Audit_reaches_other_modules_only_through_their_contracts()
    {
        var result = Types.InAssembly(AuditCore)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Dms.Identity.Domain",
                "Dms.Identity.Application",
                "Dms.Identity.Infrastructure",
                "Dms.Authorization.Domain",
                "Dms.Authorization.Application",
                "Dms.Authorization.Infrastructure")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Theory]
    [MemberData(nameof(CoreAssemblies))]
    public void Domain_types_do_not_depend_on_the_application_layer_of_their_own_module(
        string module,
        Assembly assembly)
    {
        var result = Types.InAssembly(assembly)
            .That().ResideInNamespaceEndingWith(".Domain")
            .ShouldNot().HaveDependencyOn($"Dms.{module}.Application")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Fact]
    public void Aggregate_roots_keep_their_identifiers_strongly_typed()
    {
        // Guid ids are easy to swap by accident; every aggregate uses a typed id instead.
        var offenders = Types.InAssemblies([IdentityCore, AuthorizationCore, AuditCore, DocumentsCore, DocumentTypesCore, StorageCore, WorkflowCore])
            .That().Inherit(typeof(AggregateRoot<>))
            .GetTypes()
            .Where(type => type.BaseType?.GenericTypeArguments.FirstOrDefault() == typeof(Guid))
            .Select(type => type.Name)
            .ToList();

        offenders.ShouldBeEmpty();
    }

    private static string Describe(NetArchTest.Rules.TestResult result) =>
        result.FailingTypeNames is null ? string.Empty : string.Join(", ", result.FailingTypeNames);
}
