using System.CodeDom.Compiler;
using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Resources;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace RaiNote.PacketMediator;

[Generator]
public class PacketHandlerGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var structProvider = context.SyntaxProvider.CreateSyntaxProvider(
                predicate: static (node, _) => node is StructDeclarationSyntax,
                transform: static (ctx, _) => GetAttributesInheriting(ctx)
            )
            .Where(m => m is not null);
        var classProvider = context.SyntaxProvider.CreateSyntaxProvider(
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, _) => ctx.Node as ClassDeclarationSyntax
            )
            .Where(m => m is not null);
        var compilation = context.CompilationProvider.Combine(structProvider.Collect());
        context.RegisterSourceOutput(compilation, Execute);
        var writer = new IndentedTextWriter(new StringWriter());
    }

    private static ICollection<string> GetAttributesInheriting(GeneratorSyntaxContext context)
    {
        var arrayOfInheritingTypes = new List<string>();
        var structDeclarationSyntax = (StructDeclarationSyntax)context.Node;
        foreach (var attributeList in structDeclarationSyntax.AttributeLists)
        {
            foreach (var attribute in attributeList.Attributes)
            {
                if (context.SemanticModel.GetSymbolInfo(attribute).Symbol is not IMethodSymbol attributeSymbol)
                    continue; // if we can't get the symbol, ignore it
                if (context.SemanticModel.GetSymbolInfo(attribute).Symbol is not ITypeSymbol attributeTypeSymbol)
                    continue;
                if (!attributeTypeSymbol.AllInterfaces
                        .Select(x => x.Name.Equals(typeof(PacketIdAttribute<>).ToString(), StringComparison.Ordinal))
                        .Any())
                    continue;

                arrayOfInheritingTypes.Add(attributeTypeSymbol.Name);
            }
        }

        return arrayOfInheritingTypes;
    }

    private void Execute(SourceProductionContext context,
        (Compilation Left, ImmutableArray<ICollection<string>> Right) valueTuple)
    {
        var (compilation, attributeNames) = valueTuple;
        var code = $@"{string.Join('\n', attributeNames)}";
        code += "\r\n";

        context.AddSource("Test.g.cs", SourceText.From(code, Encoding.UTF8));
    }
}
