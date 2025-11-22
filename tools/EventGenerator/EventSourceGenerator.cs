using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace EventGenerator;

[Generator]
public class EventSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Add the attribute source
        context.RegisterPostInitializationOutput(ctx => ctx.AddSource(
            "GenApiEventAttribute.g.cs",
            SourceText.From(AttributeSource, Encoding.UTF8)));

        // Find methods with the attribute
        IncrementalValuesProvider<MethodInfo> methodsWithAttribute = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => IsSyntaxTargetForGeneration(s),
                transform: static (ctx, _) => GetSemanticTargetForGeneration(ctx))
            .Where(static m => m is not null);

        // Combine with compilation
        IncrementalValueProvider<(Compilation Left, ImmutableArray<MethodInfo> Right)> compilationAndMethods = context.CompilationProvider.Combine(methodsWithAttribute.Collect());

        // Generate the source
        context.RegisterSourceOutput(compilationAndMethods,
            static (spc, source) => Execute(source.Left, source.Right, spc));
    }

    static bool IsSyntaxTargetForGeneration(SyntaxNode node) => node is MethodDeclarationSyntax m && m.AttributeLists.Count > 0;

    static MethodInfo GetSemanticTargetForGeneration(GeneratorSyntaxContext context)
    {
        var methodDeclaration = (MethodDeclarationSyntax)context.Node;
        ISymbol methodSymbol = context.SemanticModel.GetDeclaredSymbol(methodDeclaration);

        if (methodSymbol == null)
            return null;

        foreach (AttributeData attributeData in methodSymbol.GetAttributes())
        {
            if (attributeData.AttributeClass?.Name != "GenApiEventAttribute")
                continue;

            if (attributeData.ConstructorArguments.Length != 1)
                continue;

            string eventName = attributeData.ConstructorArguments[0].Value?.ToString();
            if (string.IsNullOrEmpty(eventName))
                continue;

            // Get the event args type from EventSink
            string eventArgsType = GetEventArgsType(context.SemanticModel.Compilation, eventName);
            if (string.IsNullOrEmpty(eventArgsType))
                continue;

            return new MethodInfo
            {
                MethodName = methodSymbol.Name,
                EventName = eventName,
                EventArgsType = eventArgsType,
                ClassName = methodSymbol.ContainingType.Name,
                Namespace = methodSymbol.ContainingNamespace.ToDisplayString()
            };
        }

        return null;
    }

    static string GetEventArgsType(Compilation compilation, string eventName)
    {
        INamedTypeSymbol eventSinkType = compilation.GetTypeByMetadataName("ClassicUO.Game.Managers.EventSink");
        if (eventSinkType == null)
            return "object";

        IEventSymbol eventMember = eventSinkType.GetMembers(eventName).OfType<IEventSymbol>().FirstOrDefault();
        if (eventMember == null)
            return "object";

        if (eventMember.Type is INamedTypeSymbol namedType && namedType.IsGenericType)
        {
            ITypeSymbol typeArg = namedType.TypeArguments.FirstOrDefault();
            return typeArg?.ToDisplayString() ?? "object";
        }

        return "object";
    }

    static void Execute(Compilation compilation, IEnumerable<MethodInfo> methods, SourceProductionContext context)
    {
        if (methods == null || !methods.Any())
            return;

        IEnumerable<IGrouping<(string Namespace, string ClassName), MethodInfo>> methodsByClass = methods.GroupBy(m => (m.Namespace, m.ClassName));

        foreach (IGrouping<(string Namespace, string ClassName), MethodInfo> group in methodsByClass)
        {
            string source = GeneratePartialClass(group.Key.Namespace, group.Key.ClassName, group.ToList());
            context.AddSource($"{group.Key.ClassName}.Events.g.cs", SourceText.From(source, Encoding.UTF8));
        }
    }

    static string GeneratePartialClass(string namespaceName, string className, List<MethodInfo> methods)
    {
        var sb = new StringBuilder();

        sb.AppendLine("using System;");
        sb.AppendLine("using ClassicUO.Game.Managers;");
        sb.AppendLine();
        sb.AppendLine($"namespace {namespaceName};");
        sb.AppendLine();
        sb.AppendLine($"public partial class {className}");
        sb.AppendLine("{");

        // Generate fields
        foreach (MethodInfo method in methods)
        {
            string fieldName = GetFieldName(method.EventName);
            sb.AppendLine($"    private EventHandler<{method.EventArgsType}> {fieldName};");
        }

        if (methods.Count > 0)
            sb.AppendLine();

        // Generate complete method implementations
        foreach (MethodInfo method in methods)
        {
            string fieldName = GetFieldName(method.EventName);
            string unsubscribeMethodName = $"Unsubscribe{method.EventName}";

            sb.AppendLine($"    public partial void {method.MethodName}(object callback)");
            sb.AppendLine($"    {{");
            sb.AppendLine($"        {unsubscribeMethodName}();");
            sb.AppendLine();
            sb.AppendLine($"        if (callback == null || !_engine.Operations.IsCallable(callback))");
            sb.AppendLine($"            return;");
            sb.AppendLine();
            sb.AppendLine($"        {fieldName} = (sender, arg) =>");
            sb.AppendLine($"        {{");
            sb.AppendLine($"            _api?.ScheduleCallback(callback, arg);");
            sb.AppendLine($"        }};");
            sb.AppendLine();
            sb.AppendLine($"        EventSink.{method.EventName} += {fieldName};");
            sb.AppendLine($"    }}");
            sb.AppendLine();

            // Generate unsubscribe method
            sb.AppendLine($"    private void {unsubscribeMethodName}()");
            sb.AppendLine($"    {{");
            sb.AppendLine($"        if ({fieldName} != null)");
            sb.AppendLine($"        {{");
            sb.AppendLine($"            EventSink.{method.EventName} -= {fieldName};");
            sb.AppendLine($"            {fieldName} = null;");
            sb.AppendLine($"        }}");
            sb.AppendLine($"    }}");

            if (method != methods.Last())
                sb.AppendLine();
        }

        sb.AppendLine("}");

        return sb.ToString();
    }

    static string GetFieldName(string eventName) => $"_{char.ToLower(eventName[0])}{eventName.Substring(1)}Handler";

    private const string AttributeSource = @"
using System;

namespace ClassicUO.LegionScripting.PyClasses
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    internal class GenApiEventAttribute : Attribute
    {
        public string EventName { get; }

        public GenApiEventAttribute(string eventName)
        {
            EventName = eventName;
        }
    }
}
";

    class MethodInfo
    {
        public string MethodName { get; set; }
        public string EventName { get; set; }
        public string EventArgsType { get; set; }
        public string ClassName { get; set; }
        public string Namespace { get; set; }
    }
}
