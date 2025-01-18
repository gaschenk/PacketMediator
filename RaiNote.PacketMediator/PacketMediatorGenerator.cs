// Licensed to Timothy Schenk under the Apache 2.0 License.

using System.CodeDom.Compiler;
using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
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
                predicate: (node, _) => node is StructDeclarationSyntax { AttributeLists.Count: > 0},
                transform: (syntaxContext, cancellationToken) => {
                    var structDeclaration = (StructDeclarationSyntax)syntaxContext.Node;
                    var model = syntaxContext.SemanticModel;
                    var symbol =
                        ModelExtensions.GetDeclaredSymbol(model, structDeclaration,
                                cancellationToken: cancellationToken) as
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
                    ModelExtensions.GetDeclaredSymbol(model, classDeclaration, cancellationToken: cancellationToken) as
                        INamedTypeSymbol;

                var packetStruct = (symbol?.Interfaces.Select(interfaceSyntax => {
                    if (!interfaceSyntax.Name.SequenceEqual("IPacketHandler")) {
                        return null;
                    }

                    if (interfaceSyntax.TypeArguments.Length != 2)
                        return null;

                    var genericStructArgument = interfaceSyntax.TypeArguments[0];
                    var genericSessionArgument = interfaceSyntax.TypeArguments[1];

                    return new IntermediatePacketStructHandlerData(genericStructArgument, genericSessionArgument);
                }) ?? throw new InvalidOperationException("1")).FirstOrDefault(x => x != null);

                if (packetStruct == null)
                    return null;
                return new IntermediatePacketHandlerData(symbol, classDeclaration.Identifier.Text, packetStruct);
            }).Where(result => result != null);

        var location =
            context.SyntaxProvider.CreateSyntaxProvider(predicate: static (node, _) => InterceptorPredicate(node),
                    transform: static (context, ct) => InterceptorTransform(context, ct))
                .Where(candidate => candidate is not null);

        var combinedResults = structsWithAttributes.Collect().Combine(packetHandlers.Collect());

        // Collect and generate the dictionary
        context.RegisterSourceOutput(combinedResults, (ctx, result) => {
            var (packetStructs, packetHandlerDatas) = result;

            var combinedInfo = packetHandlerDatas.Select(handler => {
                var respectiveStruct = packetStructs.FirstOrDefault(pStruct =>
                    pStruct.structName.SequenceEqual(handler?.PacketStructHandlerData?.PacketStructSymbol.Name ??
                                                     string.Empty));
                return (handler, respectiveStruct);
            });
            var packetHandlerData = packetHandlerDatas.FirstOrDefault();
            var usedValues = new List<long>();
            var highestValue = long.Parse(packetStructs.FirstOrDefault().enumMaxValue?.ToString());
            var ms = new MemoryStream();
            var sw = new StreamWriter(ms, Encoding.UTF8);
            sw.AutoFlush = true;

            var enumTypeString = packetStructs.FirstOrDefault().enumType?.ToDisplayString();
            var sessionTypeString = packetHandlerData?.PacketStructHandlerData?.SessionSymbol.ToDisplayString();
            sw.WriteLine($$"""
                          using System;
                          using System.Threading;
                          using System.Threading.Tasks;
                          public static class PacketHandlerMediator
                          {
                              public async static Task Handle(IServiceProvider serviceProvider, byte[] data,{{enumTypeString}} opcode ,{{packetHandlerData?.PacketStructHandlerData?.SessionSymbol}} session, CancellationToken cancellationToken){
                              switch(opcode)
                              {
                          """);

            foreach (var ((handler, (symbol, structName, value, _, enumMember, _, implementsInterface)), i) in
                     combinedInfo.Select((value, i)=> (value, i))) {
                if (!implementsInterface) {
                    var diagnostic = Diagnostic.Create(_rpmGen001Diagnostic, symbol?.Locations.FirstOrDefault(),
                        structName);

                    ctx.ReportDiagnostic(diagnostic);
                    continue;
                }

                var tempVal = long.Parse(value?.ToString()!,
                    new NumberFormatInfo());
                usedValues.Add(tempVal);
                sw.WriteLine($"""
                                    case {enumMember}:
                                      var packet = new {handler?.PacketStructHandlerData?.PacketStructSymbol.ToDisplayString()}();
                                      packet.Deserialize(data);
                                      _ = {handler?.Symbol?.ToDisplayString()}.HandleAsync(packet, session, cancellationToken);
                                      return;
                              """);
            }

            // Forced jumptable on asm generation
            for (long i = 0; i <= highestValue; i++) {
                if (!usedValues.Contains(i)) {
                    sw.WriteLine($"""
                                        case (({enumTypeString}){i}):
                                          _ =  StubHandler.HandleAsync(data, opcode, session, cancellationToken);
                                          return;
                                  """);
                }
            }

            // TODO: allow overriding
            sw.WriteLine($$"""
                                 }
                               }
                           }
                           public static class StubHandler {
                               public static async Task HandleAsync(byte[] data,{{enumTypeString}} opcode, {{sessionTypeString}} session, CancellationToken cancellationToken) {
                               // Stub method
                               }
                           }
                           """);

            sw.Flush();
            ctx.AddSource("PacketHandlerMediator.g.cs", SourceText.From(sw.BaseStream, Encoding.UTF8, canBeEmbedded: true));
            sw.Close();
            ms.Close();
            var stringWriter = new StringWriter();
            var idWriter = new IndentedTextWriter(stringWriter);
            idWriter.WriteLine("using Microsoft.Extensions.DependencyInjection;");
            idWriter.WriteLine("public static class ServiceExtensions{");
            idWriter.Indent++;
            idWriter.WriteLine("public static void AddPacketHandlerServices(this IServiceCollection serviceCollection){");
            idWriter.Indent++;
            idWriter.WriteLine("// PacketHandler Service Generation");
            foreach (var handlerData in packetHandlerDatas) {
                idWriter.WriteLine($"serviceCollection.AddScoped<{handlerData?.Symbol?.ToDisplayString()}>();");
            }
            idWriter.Indent--;
            idWriter.WriteLine("}");
            idWriter.Indent--;
            idWriter.WriteLine("}");
            ctx.AddSource("ServiceExtensions.g.cs", SourceText.From(stringWriter.ToString(),Encoding.UTF8));
        });
    }

    // https://andrewlock.net/creating-a-source-generator-part-11-implementing-an-interceptor-with-a-source-generator/
    private static CandidateInvocation? InterceptorTransform(GeneratorSyntaxContext context, CancellationToken cancellationToken) {
        if (context.Node is InvocationExpressionSyntax {
                Expression: MemberAccessExpressionSyntax { Name: { } nameSyntax }
            } invocation &&
            context.SemanticModel.GetOperation(context.Node, cancellationToken) is IInvocationOperation targetOperation &&
            targetOperation.TargetMethod is
                { Name: "AddPacketHandlerServices", ContainingNamespace: { Name: "RaiNote.PacketMediator" } }
           ) {
#pragma warning disable RSEXPERIMENTAL002 // / Experimental interceptable location API
            if (context.SemanticModel.GetInterceptableLocation(invocation, cancellationToken: cancellationToken) is { } location) {
                // Return the location details and the full type details
                return new CandidateInvocation(location);
            }
#pragma warning restore RSEXPERIMENTAL002
        }

        return null;
    }

    private static bool InterceptorPredicate(SyntaxNode node) {
        return node is InvocationExpressionSyntax {
            Expression: MemberAccessExpressionSyntax {
                Name.Identifier.ValueText: "AddPacketHandlerServices"
            }
        };
    }

#pragma warning disable RSEXPERIMENTAL002 // / Experimental interceptable location API
    public record CandidateInvocation(InterceptableLocation Location);
#pragma warning restore RSEXPERIMENTAL002
}
