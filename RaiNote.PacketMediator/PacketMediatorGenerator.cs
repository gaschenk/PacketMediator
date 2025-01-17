// Licensed to Timothy Schenk under the Apache 2.0 License.

using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace RaiNote.PacketMediator;

[Generator]
public class PacketMediatorGenerator : IIncrementalGenerator {
    private readonly DiagnosticDescriptor _rpmGen001Diagnostic = new(
        id: "RPMGen001",
        title: "Struct does not implement required interface",
        messageFormat:
        "The struct '{0}' must implement at least one of:  'IPacket', 'IIncomingPacket', 'IOutgoingPacket', 'IBidirectionalPacket'",
        category: "SourceGenerator",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context) {
        context.RegisterPostInitializationOutput(ctx => {
            ctx.AddSource("PacketMediatorStatic.g.cs", SourceText.From(@"
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

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
                predicate: (node, _) => node is StructDeclarationSyntax { AttributeLists.Count: > 0 },
                transform: (syntaxContext, cancellationToken) => {
                    var structDeclaration = (StructDeclarationSyntax)syntaxContext.Node;
                    var model = syntaxContext.SemanticModel;
                    var symbol =
                        model.GetDeclaredSymbol(structDeclaration, cancellationToken: cancellationToken) as
                            INamedTypeSymbol;
                    var requiredInterfaces = new[] {
                        "IPacket", "IIncomingPacket", "IOutgoingPacket", "IBidirectionalPacket"
                    };
                    var implementsInterface = symbol != null && symbol.AllInterfaces
                        .Any(i => requiredInterfaces.Contains(i.Name, StringComparer.Ordinal));
                    if (!implementsInterface)
                        return default;
                    // Check for the marker attribute
                    var attribute = symbol?.GetAttributes()
                        .FirstOrDefault(attr => {
                            var attrClass = attr.AttributeClass;
                            while (attrClass != null) {
                                if (string.Equals(attrClass.Name, "PacketIdAttribute", StringComparison.Ordinal) &&
                                    string.Equals(attrClass.ContainingNamespace.ToDisplayString(),
                                        "RaiNote.PacketMediator", StringComparison.Ordinal)) {
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
                    return (symbol, structName: structDeclaration.Identifier.Text, value: enumValue, enumType,
                        enumMember,
                        enumMaxValue, implementsInterface);
                })
            .Where(result => result != default);
        var packetHandlers = context.SyntaxProvider.CreateSyntaxProvider(
            predicate: (node, _) => node is ClassDeclarationSyntax,
            (syntaxContext, cancellationToken) => {
                var classDeclaration = (ClassDeclarationSyntax)syntaxContext.Node;
                var model = syntaxContext.SemanticModel;
                var symbol =
                    model.GetDeclaredSymbol(classDeclaration, cancellationToken: cancellationToken) as INamedTypeSymbol;

                var packetStruct = (symbol?.Interfaces.Select(interfaceSyntax => {
                    if (!interfaceSyntax.Name.SequenceEqual("IPacketHandler")) {
                        return null;
                    }

                    if (interfaceSyntax.TypeArguments.Length < 2)
                        return null;
                    var genericStructArgument = interfaceSyntax.TypeArguments[0];
                    var genericSessionArgument = interfaceSyntax.TypeArguments[1];
                    return new IntermediatePacketStructHandlerData(genericStructArgument, genericSessionArgument);
                }) ?? throw new InvalidOperationException("1")).FirstOrDefault(x => x != null);


                return new IntermediatePacketHandlerData(symbol, classDeclaration.Identifier.Text, packetStruct);
            }).Where(result => result != null);

        var combinedResults = structsWithAttributes.Collect().Combine(packetHandlers.Collect());

        // Collect and generate the dictionary
        context.RegisterSourceOutput(combinedResults, (ctx, result) => {
            var (packetStructs, packetHandlerDatas) = result;

            var combinedInfo = packetHandlerDatas.Select(handler => {
                var respectiveStruct = packetStructs.FirstOrDefault(pStruct =>
                    pStruct.structName.SequenceEqual(handler?.PacketStructHandlerData?.PacketStructSymbol.Name));
                return (handler, respectiveStruct);
            });
            var usedValues = new List<long>();
            var highestValue = long.Parse(packetStructs.FirstOrDefault().enumMaxValue?.ToString());

            var sb = new StringBuilder();
            foreach (var @struct in packetStructs) {
                sb.Append($"// {@struct} a {@struct.enumMaxValue}");
            }

            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine();
            sb.AppendLine("public static class PacketHandlerMediator");
            sb.AppendLine("{");
            var enumTypeString = packetStructs.FirstOrDefault().enumType?.ToDisplayString();
            sb.AppendLine("    public static bool Handle()");
            sb.AppendLine($"    var x = new Dictionary<string, {enumTypeString}>()");
            sb.AppendLine("    {");

            foreach (var (handler, (symbol, structName, value, _, enumMember, _, implementsInterface)) in
                     combinedInfo) {
                if (!implementsInterface) {
                    var diagnostic = Diagnostic.Create(
                        _rpmGen001Diagnostic,
                        symbol?.Locations.FirstOrDefault(), structName);

                    ctx.ReportDiagnostic(diagnostic);
                    continue;
                }

                var tempVal = long.Parse(value?.ToString() ?? throw new InvalidOperationException("3"),
                    new NumberFormatInfo());
                usedValues.Add(tempVal);
                sb.AppendLine(
                    $"        {{ \"{handler?.PacketHandlerIdentifier}-{structName}{highestValue}\", {enumMember} }},");
            }


            for (long i = 0; i <= highestValue; i++) {
                if (!usedValues.Contains(i))
                    sb.AppendLine($"        {{ \"Dead\", (({enumTypeString}){i}) }},");
            }

            sb.AppendLine("    };");
            sb.AppendLine("}");

            ctx.AddSource("StructDictionary.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
        });
    }
}
