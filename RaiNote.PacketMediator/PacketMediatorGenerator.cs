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
            ctx.AddSource("PacketMediatorStatic.g.cs",
                SourceText.From("""

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
                                        activity?.AddTag("Handler", ToString());
                                        activity?.AddTag("Packet", packet.ToString());
                                        await HandleAsync(tPacket, session, cancellationToken);

                                        return true;
                                    }

                                    public Task HandleAsync(TIncomingPacket packet, TSession session, CancellationToken cancellationToken);
                                }

                                public interface IPacketHandler<in TSession>
                                {
                                    Task<bool> TryHandleAsync(IIncomingPacket packet, TSession session, CancellationToken cancellationToken);
                                }

                                """, Encoding.UTF8));
        });
        // Find all struct declarations
        var structsWithAttributes = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: (node, _) => node is StructDeclarationSyntax { AttributeLists.Count: > 0 },
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
                    if (!implementsInterface) {
                        // TODO: https://github.com/dotnet/roslyn/blob/main/docs/features/incremental-generators.cookbook.md#issue-diagnostics
                        // or Analyzer
                        var diagnostic = Diagnostic.Create(_rpmGen001Diagnostic, symbol?.Locations.First(),
                            symbol?.ToDisplayString());
                    }

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
                        return null;
                    }

                    var attributeConstructorArgument = attribute.ConstructorArguments[0];
                    var enumType = attributeConstructorArgument.Type;
                    var enumValue = attributeConstructorArgument.Value;

                    var enumMember = enumType?.GetMembers()
                        .OfType<IFieldSymbol>()
                        .FirstOrDefault(f => f.ConstantValue?.Equals(enumValue) == true);

                    var enumMaxValue = enumType?.GetMembers()
                        .OfType<IFieldSymbol>().Max(x => x.ConstantValue);

                    if (symbol == null || enumMember == null || enumMaxValue == null || enumType == null ||
                        enumValue == null)
                        return null;

                    var intermediatePacketStructData = new IntermediatePacketStructData(symbol.Locations.First(),
                        symbol.ToDisplayString(),
                        enumValue.ToString(),
                        enumType.ToDisplayString(),
                        enumMember.ToDisplayString(),
                        enumMaxValue.ToString(), implementsInterface);

                    return intermediatePacketStructData;
                })
            .Where(result => result != null);

        var packetHandlerValues = context.SyntaxProvider.CreateSyntaxProvider(
            predicate: (node, _) => node is ClassDeclarationSyntax,
            TransformPacketHandlers).Where(result => result != null);

        var location =
            context.SyntaxProvider.CreateSyntaxProvider(predicate: static (node, _) => InterceptorPredicate(node),
                    transform: static (context, ct) => InterceptorTransform(context, ct))
                .Where(candidate => candidate is not null);

        var combinedResults = structsWithAttributes.Collect().Combine(packetHandlerValues.Collect()).Select(
            static (tuple, cancellationToken) => {
                var (structDatas, handlerDatas) = tuple;

                var matchingData = handlerDatas.Select(handlerData => {
                    if (handlerData == null)
                        return null;
                    var structs = structDatas.Where(sData => {
                        var equals =
                            sData != null && sData.PacketStructFullIdentifier.Equals(handlerData.PacketStructHandlerData
                                ?.PacketStructFullIdentifier);

                        return equals;
                    }).FirstOrDefault();
                    if (structs == null)
                        return null;
                    var intermediateHandlerAndStructTuple = new IntermediateHandlerAndStructTuple(handlerData, structs);
                    return intermediateHandlerAndStructTuple;
                });

                var intermediateHandlerAndStructTuples = matchingData.Where(x => x != null);
                return intermediateHandlerAndStructTuples;
            });

        // Collect and generate the dictionary
        context.RegisterSourceOutput(combinedResults, (ctx, result) => {
            var combinedInfo = result.ToList();

            if (combinedInfo.Count <= 0)
                return;

            var packetHandlerData = combinedInfo.First()?.HandlerData;
            var usedValues = new List<long>();
            var intermediatePacketStructData = combinedInfo.First()?.StructData;
            if (intermediatePacketStructData?.EnumMaxValue == null)
                return;
            var highestValue = long.Parse(intermediatePacketStructData.EnumMaxValue);
            var ms = new MemoryStream();
            var sw = new StreamWriter(ms, Encoding.UTF8);
            sw.AutoFlush = true;

            var enumTypeString = intermediatePacketStructData?.EnumTypeFullIdentifier;
            var sessionTypeString = packetHandlerData?.PacketStructHandlerData?.SessionFullIdentifier;
            sw.WriteLine($$"""
                           using System;
                           using System.Threading;
                           using System.Threading.Tasks;
                           public static class PacketHandlerMediator
                           {
                               public async static Task Handle(IServiceProvider serviceProvider, byte[] data,{{enumTypeString}} opcode ,{{packetHandlerData?.PacketStructHandlerData?.SessionFullIdentifier}} session, CancellationToken cancellationToken){
                               switch(opcode)
                               {
                           """);

            var valueTuples = combinedInfo.Select((value, i) => (value, i));
            foreach (var ((handlerData, packetStructData), i) in valueTuples) {
                var tempVal = long.Parse(packetStructData.EnumValue,
                    new NumberFormatInfo());
                usedValues.Add(tempVal);
                sw.WriteLine($"""
                                    case {packetStructData.EnumMemberIdentifier}:
                                      var packet = new {handlerData.PacketHandlerIdentifier}();
                                      packet.Deserialize(data);
                                      _ = {handlerData.PacketHandlerIdentifier}.HandleAsync(packet, session, cancellationToken);
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
            ctx.AddSource("PacketHandlerMediator.g.cs",
                SourceText.From(sw.BaseStream, Encoding.UTF8, canBeEmbedded: true));
            sw.Close();
            ms.Close();
            var stringWriter = new StringWriter();
            var idWriter = new IndentedTextWriter(stringWriter);
            idWriter.WriteLine("using Microsoft.Extensions.DependencyInjection;");
            idWriter.WriteLine("public static class ServiceExtensions{");
            idWriter.Indent++;
            idWriter.WriteLine(
                "public static void AddPacketHandlerServices(this IServiceCollection serviceCollection){");
            idWriter.Indent++;
            idWriter.WriteLine("// PacketHandler Service Generation");
            foreach (var (handlerData, _) in combinedInfo) {
                idWriter.WriteLine($"serviceCollection.AddScoped<{handlerData.PacketHandlerIdentifier}>();");
            }

            idWriter.Indent--;
            idWriter.WriteLine("}");
            idWriter.Indent--;
            idWriter.WriteLine("}");
            ctx.AddSource("ServiceExtensions.g.cs", SourceText.From(stringWriter.ToString(), Encoding.UTF8));
        });
    }

    private static IntermediatePacketHandlerData? TransformPacketHandlers(GeneratorSyntaxContext syntaxContext,
        CancellationToken cancellationToken) {
        var classDeclaration = (ClassDeclarationSyntax)syntaxContext.Node;
        var model = syntaxContext.SemanticModel;
        var symbol =
            ModelExtensions.GetDeclaredSymbol(model, classDeclaration, cancellationToken: cancellationToken) as
                INamedTypeSymbol;
        var packetStruct = (symbol.Interfaces.Select(interfaceSyntax => {
            if (!interfaceSyntax.Name.Equals("IPacketHandler")) {
                return null;
            }

            if (interfaceSyntax.TypeArguments.Length != 2) return null;

            var genericStructArgument = interfaceSyntax.TypeArguments[0];
            var genericSessionArgument = interfaceSyntax.TypeArguments[1];

            return new IntermediatePacketStructHandlerData(genericStructArgument.ToDisplayString(),
                genericSessionArgument.ToDisplayString());
        }) ?? throw new InvalidOperationException("1")).FirstOrDefault(x => x != null);

        if (packetStruct == null) return null;
        var intermediatePacketHandlerData = new IntermediatePacketHandlerData(symbol.ToDisplayString(), packetStruct);

        return intermediatePacketHandlerData;
    }

    // https://andrewlock.net/creating-a-source-generator-part-11-implementing-an-interceptor-with-a-source-generator/
    private static CandidateInvocation? InterceptorTransform(GeneratorSyntaxContext context,
        CancellationToken cancellationToken) {
        if (context.Node is InvocationExpressionSyntax {
                Expression: MemberAccessExpressionSyntax { Name: { } nameSyntax }
            } invocation &&
            context.SemanticModel.GetOperation(context.Node,
                cancellationToken) is IInvocationOperation targetOperation &&
            targetOperation.TargetMethod is
                { Name: "AddPacketHandlerServices", ContainingNamespace: { Name: "RaiNote.PacketMediator" } }
           ) {
#pragma warning disable RSEXPERIMENTAL002 // / Experimental interceptable location API
            if (context.SemanticModel.GetInterceptableLocation(invocation, cancellationToken: cancellationToken) is
                { } location) {
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
