using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace UnrealSharp.SourceGenerators;

public class DelegateInheritanceSyntaxReceiver : ISyntaxReceiver
{
    public List<ClassDeclarationSyntax> CandidateClasses { get; } = [];

    public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
    {
        if (syntaxNode is ClassDeclarationSyntax { BaseList: not null } classDecl &&
            classDecl.BaseList.Types.Any(bt => bt.Type.ToString().Contains("MulticastDelegate")))
        {
            CandidateClasses.Add(classDecl);
        }
    }
}

[Generator]
public class DelegateWrapperGenerator : ISourceGenerator
{
    public void Initialize(GeneratorInitializationContext context)
    {
        context.RegisterForSyntaxNotifications(() => new DelegateInheritanceSyntaxReceiver());
    }

    public void Execute(GeneratorExecutionContext context)
    {
        if (context.SyntaxReceiver is not DelegateInheritanceSyntaxReceiver receiver)
        {
            return;
        }
        
        foreach (var classDecl in receiver.CandidateClasses)
        {
            // Obtain the SemanticModel for the current syntax tree
            var model = context.Compilation.GetSemanticModel(classDecl.SyntaxTree);

            // Get the symbol for the class declaration

            if (model.GetDeclaredSymbol(classDecl) is not INamedTypeSymbol classSymbol)
            {
                continue;
            }
            
            // Check if the class inherits from MulticastDelegate
            if (classSymbol.BaseType.Name != "MulticastDelegate" || classSymbol.BaseType is not INamedTypeSymbol multicastDelegateSymbol)
            {
                continue;
            }
            // Extract the generic type argument
            var genericTypeArgument = multicastDelegateSymbol.TypeArguments.FirstOrDefault();
            
            if (genericTypeArgument == null)
            {
                continue;
            }
            
            var typeSymbol = context.Compilation.GetSemanticModel(classDecl.SyntaxTree).GetDeclaredSymbol(classDecl);
            string namespaceName = typeSymbol?.ContainingNamespace.ToDisplayString() ?? "Global";
            string className = $"{classDecl.Identifier.ValueText}";
            
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.AppendLine("using UnrealSharp;");
            stringBuilder.AppendLine("using UnrealSharp.Interop;");
            stringBuilder.AppendLine();
            stringBuilder.AppendLine($"namespace {namespaceName};");
            stringBuilder.AppendLine();
                
            stringBuilder.AppendLine($"public partial class {className}");
            stringBuilder.AppendLine("{");
            
            INamedTypeSymbol delegateSymbol = (INamedTypeSymbol) genericTypeArgument;
            GenerateAddFunction(stringBuilder, delegateSymbol);
            GenerateRemoveFunction(stringBuilder, delegateSymbol);
                    
            stringBuilder.AppendLine("}");
                    
            string source = stringBuilder.ToString();
            context.AddSource($"{className}.generated.cs", SourceText.From(source, Encoding.UTF8));
        }
    }

    void GenerateAddFunction(StringBuilder stringBuilder, INamedTypeSymbol delegateSymbol)
    {
        var delegateInvokeMethod = delegateSymbol.DelegateInvokeMethod;

        if (delegateInvokeMethod == null)
        {
            return;
        }
        
        if (delegateInvokeMethod.Parameters.IsEmpty)
        {
            stringBuilder.AppendLine("    public void Add(Action action)");
        }
        else
        {
            stringBuilder.Append("    public void Add(Action<");
            stringBuilder.Append(string.Join(", ", delegateInvokeMethod.Parameters.Select(x => $"{x.Type}")));
            stringBuilder.Append("> action)");
            stringBuilder.AppendLine();
        }
        
        stringBuilder.AppendLine("    {");
        stringBuilder.AppendLine("        try");
        stringBuilder.AppendLine("        {");
        CastToUnrealSharpObject(stringBuilder);
        stringBuilder.AppendLine($"            FMulticastDelegatePropertyExporter.CallAddDelegate(NativeDelegate, unrealSharpObject.NativeObject, action.Method.Name);");
        stringBuilder.AppendLine("        }");
        stringBuilder.AppendLine("        catch (Exception ex)");
        stringBuilder.AppendLine("        {");
        stringBuilder.AppendLine("            Console.WriteLine(ex);");
        stringBuilder.AppendLine("        }");
        stringBuilder.AppendLine("    }");
        stringBuilder.AppendLine();
    }

    void GenerateRemoveFunction(StringBuilder stringBuilder, INamedTypeSymbol delegateSymbol)
    {
        var delegateInvokeMethod = delegateSymbol.DelegateInvokeMethod;

        if (delegateInvokeMethod == null)
        {
            return;
        }
        
        if (delegateInvokeMethod.Parameters.IsEmpty)
        {
            stringBuilder.AppendLine($"    public void Remove(Action action)");
        }
        else
        {
            stringBuilder.Append("    public void Remove(Action<");
            stringBuilder.Append(string.Join(", ", delegateInvokeMethod.Parameters.Select(x => $"{x.Type}")));
            stringBuilder.Append("> action)");
            stringBuilder.AppendLine();
        }
        
        stringBuilder.AppendLine("    {");
        stringBuilder.AppendLine("        try");
        stringBuilder.AppendLine("        {");
        CastToUnrealSharpObject(stringBuilder);
        stringBuilder.AppendLine($"            FMulticastDelegatePropertyExporter.CallRemoveDelegate(NativeDelegate, unrealSharpObject.NativeObject, action.Method.Name);");
        stringBuilder.AppendLine("        }");
        stringBuilder.AppendLine("        catch (Exception ex)");
        stringBuilder.AppendLine("        {");
        stringBuilder.AppendLine("            Console.WriteLine(ex);");
        stringBuilder.AppendLine("        }");
        stringBuilder.AppendLine("    }");
        stringBuilder.AppendLine();
    }

    void CastToUnrealSharpObject(StringBuilder stringBuilder)
    {
        stringBuilder.AppendLine("            UnrealSharpObject unrealSharpObject = (UnrealSharpObject) action.Target;");
        stringBuilder.AppendLine("            if (unrealSharpObject == null)");
        stringBuilder.AppendLine("            {");
        stringBuilder.AppendLine("                return;");
        stringBuilder.AppendLine("            }");
    }
}