using System.Reflection;
using FreightFlow.WorkflowWorker.Application;
using Shouldly;

namespace FreightFlow.Domain.Tests;

/// <summary>
/// Enforces the Clean Architecture boundary in FreightFlow.WorkflowWorker:
/// Domain/ and Application/ must have zero direct type references to Infrastructure/.
///
/// Checks fields, properties, method return types, and method parameters on every
/// type whose namespace begins with the protected namespaces.
/// </summary>
public sealed class ArchitectureTests
{
    private static readonly Assembly WorkflowAssembly =
        typeof(AwardWorkflowState).Assembly;  // FreightFlow.WorkflowWorker

    private const string InfraNamespace = "FreightFlow.WorkflowWorker.Infrastructure";

    private static readonly string[] ProtectedNamespaces =
    [
        "FreightFlow.WorkflowWorker.Domain",
        "FreightFlow.WorkflowWorker.Application"
    ];

    [Fact]
    public void WorkflowWorker_Domain_And_Application_Have_No_References_To_Infrastructure()
    {
        var violations = new List<string>();

        foreach (var type in WorkflowAssembly.GetTypes())
        {
            if (!ProtectedNamespaces.Any(ns => type.Namespace?.StartsWith(ns) == true))
                continue;

            const BindingFlags all =
                BindingFlags.Instance | BindingFlags.Static |
                BindingFlags.Public   | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly;

            // Fields
            foreach (var field in type.GetFields(all))
            {
                if (ReferencesInfra(field.FieldType))
                    violations.Add(
                        $"FIELD   {type.FullName}.{field.Name} : {field.FieldType.FullName}");
            }

            // Properties
            foreach (var prop in type.GetProperties(all))
            {
                if (ReferencesInfra(prop.PropertyType))
                    violations.Add(
                        $"PROP    {type.FullName}.{prop.Name} : {prop.PropertyType.FullName}");
            }

            // Methods — return type and parameters
            foreach (var method in type.GetMethods(all))
            {
                if (ReferencesInfra(method.ReturnType))
                    violations.Add(
                        $"RETURN  {type.FullName}.{method.Name}() : {method.ReturnType.FullName}");

                foreach (var param in method.GetParameters())
                {
                    if (ReferencesInfra(param.ParameterType))
                        violations.Add(
                            $"PARAM   {type.FullName}.{method.Name}({param.Name}) : {param.ParameterType.FullName}");
                }
            }
        }

        violations.ShouldBeEmpty(
            "Domain and Application layers must not reference any Infrastructure type. " +
            "Move the dependency inversion to an interface in Application/ActivityContracts.cs.");
    }

    private static bool ReferencesInfra(Type type)
        => type.Namespace?.StartsWith(InfraNamespace) == true;
}
