// Licensed to Timothy Schenk under the Apache 2.0 License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace PacketMediator.Generator;

[Generator]
public class PacketMediatorGenerator : IIncrementalGenerator {
    public void Initialize(IncrementalGeneratorInitializationContext context) {
        context.RegisterPostInitializationOutput(ctx => {
            ctx.AddSource("PacketMediatorStatic.g.cs", SourceText.From(@"
using System;
using System.Diagnostics;

namespace RaiNote.PacketMediator;
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public abstract class PacketIdAttribute<TPacketIdEnum> : Attribute where TPacketIdEnum : Enum
{
    protected PacketIdAttribute(TPacketIdEnum code)
    {
        Code = code;
    }

    public TPacketIdEnum Code { get; }
}
public interface IPacket;
public interface IIncomingPacket : IPacket
{
    public void Deserialize(byte[] data);
}
public interface IOutgoingPacket : IPacket
{
    public byte[] Serialize();
}

public interface IBidirectionalPacket : IOutgoingPacket, IIncomingPacket;

public interface IPacketHandler<in TIncomingPacket, in TSession> : IPacketHandler<TSession>
    where TIncomingPacket : IIncomingPacket
{
    async Task<bool> IPacketHandler<TSession>.TryHandleAsync(IIncomingPacket packet, TSession session,
        CancellationToken cancellationToken)
    {
        if (packet is not TIncomingPacket tPacket)
        {
            return false;
        }

        using var activity = new ActivitySource(nameof(PacketMediator)).StartActivity(nameof(HandleAsync));
        activity?.AddTag(""Handler"", ToString());
        activity?.AddTag(""Packet"", packet.ToString());
        await HandleAsync(tPacket, session, cancellationToken);

        return true;
    }

    [UsedImplicitly(ImplicitUseTargetFlags.WithInheritors)]
    public Task HandleAsync(TIncomingPacket packet, TSession session, CancellationToken cancellationToken);
}

public interface IPacketHandler<in TSession>
{
    Task<bool> TryHandleAsync(IIncomingPacket packet, TSession session, CancellationToken cancellationToken);
}

", Encoding.UTF8));
        });
        // Find all struct declarations
        var structsWithAttributes = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: (node, _) => node is StructDeclarationSyntax structSyntax &&
                                        structSyntax.AttributeLists.Count > 0,
                transform: (syntaxContext, _) => {
                    var structDeclaration = (StructDeclarationSyntax)syntaxContext.Node;
                    var model = syntaxContext.SemanticModel;
                    var symbol = model.GetDeclaredSymbol(structDeclaration) as INamedTypeSymbol;
                    var requiredInterfaces = new[] { "IPacket", "IIncomingPacket", "IOutgoingPacket", "IBidirectionalPacket" };
                    var implementsInterface = symbol != null && symbol.AllInterfaces
                        .Any(i => requiredInterfaces.Contains(i.Name));
                    // Check for the marker attribute
                    var attribute = symbol?.GetAttributes()
                        .FirstOrDefault(attr =>
                        {
                            var attrClass = attr.AttributeClass;
                            while (attrClass != null)
                            {
                                if (attrClass.Name == "PacketIdAttribute" && attrClass.ContainingNamespace.ToDisplayString() == "PacketMediator.Generator")
                                {
                                    return true;
                                }
                                attrClass = attrClass.BaseType;
                            }
                            return false;
                        });
                    if (attribute == null) {
                        return default;
                    }

                    var attributeConstructorArgument = attribute.ConstructorArguments[0];
                    var enumType = attributeConstructorArgument.Type;
                    var enumValue = attributeConstructorArgument.Value;
                    var enumMember = enumType?.GetMembers()
                        .OfType<IFieldSymbol>()
                        .FirstOrDefault(f => f.ConstantValue?.Equals(enumValue) == true);
                    var enumMaxValue = enumType?.GetMembers()
                        .OfType<IFieldSymbol>().Max(x => x.ConstantValue);
                    return (symbol, structDeclaration.Identifier.Text, value: enumValue,enumType,enumMember,enumMaxValue, implementsInterface);

                })
            .Where(result => result != default);

        // Collect and generate the dictionary
        context.RegisterSourceOutput(structsWithAttributes.Collect(), (ctx, result) => {
            var usedValues = new List<long>();
            var highestValue = long.Parse(result.FirstOrDefault().enumMaxValue?.ToString() ?? throw new InvalidOperationException());

            var sb = new StringBuilder();
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine();
            sb.AppendLine("public static class StructDictionary");
            sb.AppendLine("{");
            var enumTypeString = result.FirstOrDefault().enumType?.ToDisplayString();
            sb.AppendLine($"    public static readonly Dictionary<string, {enumTypeString}> Values = new()");
            sb.AppendLine("    {");

            foreach (var (symbol, structName, value, _,enumMember,_, implementsInterface) in result) {
                if (!implementsInterface) {
                    var diagnostic = Diagnostic.Create(
                        new DiagnosticDescriptor(
                            id: "MYGEN001",
                            title: "Struct does not implement required interface",
                            messageFormat: $"The struct '{{0}}' must implement at least one of:  \"IPacket\", \"IIncomingPacket\", \"IOutgoingPacket\", \"IBidirectionalPacket\" ",
                            category: "SourceGenerator",
                            DiagnosticSeverity.Error,
                            isEnabledByDefault: true),
                        symbol?.Locations.FirstOrDefault(), structName);

                    ctx.ReportDiagnostic(diagnostic);
                    continue;
                }
                var tempVal = long.Parse(value?.ToString() ?? throw new InvalidOperationException());
                usedValues.Add(tempVal);
                sb.AppendLine($"        {{ \"{structName}{highestValue}\", {enumMember} }},");
            }


            for (long i = 0; i <= highestValue; i++) {
                if(!usedValues.Contains(i))
                    sb.AppendLine($"        {{ \"Dead\", (({enumTypeString}){i}) }},");
            }

            sb.AppendLine("    };");
            sb.AppendLine("}");

            ctx.AddSource("StructDictionary.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
        });
    }
}
