// Licensed to Timothy Schenk under the Apache 2.0 License.

using System.CodeDom.Compiler;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace RaiNote.PacketMediator;

[Generator]
public class PacketMediatorGenerator : IIncrementalGenerator {
    private readonly DiagnosticDescriptor _rpmGen001Diagnostic = new(
        "RPMGen001",
        "Struct does not implement required interface",
        "The struct '{0}' must implement at least one of:  'IPacket', 'IIncomingPacket', 'IOutgoingPacket', 'IBidirectionalPacket'",
        "SourceGenerator",
        DiagnosticSeverity.Error,
        true);

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
                                    protected PacketIdAttribute(TPacketIdEnum code, int maximumPacketLength)
                                    {
                                        Code = code;
                                        MaximumPacketLength = maximumPacketLength;
                                    }

                                    public TPacketIdEnum Code { get; }
                                    public int MaximumPacketLength { get; }
                                }
                                public enum PacketSerializationCodes{
                                    Valid,
                                    Invalid,
                                    InvalidSize
                                }
                                public interface IPacket
                                {
                                    public int GetCurrentMaxSize();
                                }
                                public interface IIncomingPacket : IPacket
                                {
                                    public PacketSerializationCodes Deserialize(Span<byte> data);
                                }
                                public interface IOutgoingPacket : IPacket
                                {
                                    public PacketSerializationCodes Serialize(Span<byte> data);
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
                (node, _) => node is StructDeclarationSyntax { AttributeLists.Count: > 0 },
                TransformPacketStructs)
            .Where(result => result != null);

        var packetHandlerValues = context.SyntaxProvider.CreateSyntaxProvider(
                (node, _) => node is ClassDeclarationSyntax,
                TransformPacketHandlers)
            .Where(result => result != null);

        var combinedResults = structsWithAttributes.Collect()
            .Combine(packetHandlerValues.Collect())
            .Select(ToIntermediateTuple);

        // Collect and generate the dictionary
        context.RegisterSourceOutput(combinedResults, (ctx, result) => {
            var combinedInfo = result.Where(x => x != null)
                .Select(IntermediateHandlerAndStructTuple (x) => x!)
                .ToList();

            if (combinedInfo.Count <= 0) {
                return;
            }

            var packetHandlerData = combinedInfo.First()
                ?.HandlerData;
            var usedValues = new List<long>();
            var intermediatePacketStructData = combinedInfo[0]?.StructData;
            if (intermediatePacketStructData?.EnumMaxValue == null) {
                return;
            }

            var highestValue = long.Parse(intermediatePacketStructData.EnumMaxValue, NumberStyles.Integer,
                new NumberFormatInfo());
            var ms = new MemoryStream();
            var sw = new StreamWriter(ms, Encoding.UTF8);
            sw.AutoFlush = true;

            var enumTypeString = intermediatePacketStructData.EnumTypeFullIdentifier;
            var sessionTypeString = packetHandlerData?.PacketStructHandlerData?.SessionFullIdentifier;
            sw.WriteLine($$"""
                           using System;
                           using System.Threading;
                           using System.Threading.Tasks;
                           using Microsoft.Extensions.DependencyInjection;
                           using System.Runtime.CompilerServices;

                           public static class PacketHandlerMediator
                           {
                               public async static Task Handle(IServiceProvider serviceProvider, byte[] data, {{enumTypeString}} opcode ,{{packetHandlerData?.PacketStructHandlerData?.SessionFullIdentifier}} session, CancellationToken cancellationToken){

                               switch(opcode)
                               {
                           """);

            foreach (var (_, packetStructData) in combinedInfo) {
                if (packetStructData.EnumValue != null) {
                    var tempVal = long.Parse(packetStructData.EnumValue,
                        new NumberFormatInfo());
                    usedValues.Add(tempVal);
                }

                // Fixed number of operations per case (2) => see StubHandler
                sw.WriteLine($"""
                                    case {packetStructData.EnumMemberIdentifier}:
                                        await KnownHandlerMethodDump.Handle{packetStructData.EnumValue}(serviceProvider, data, opcode, session, cancellationToken);
                                        return;
                              """);
            }

            // Forced jump-table on asm/il generation
            for (long i = 0; i <= highestValue; i++) {
                if (!usedValues.Contains(i)) {
                    sw.WriteLine($"""
                                        case (({enumTypeString}){i}):
                                            await StubHandler.HandleAsync(data, opcode, session, cancellationToken);
                                            return;
                                  """);
                }
            }

            sw.WriteLine("""
                               }
                             }
                         }
                         static class KnownHandlerMethodDump {
                         """);

            // Generate methods to have uniform number of operations per case
            foreach (var (handlerData, packetStructData) in combinedInfo) {
                if (packetStructData.EnumValue != null) {
                    var tempVal = long.Parse(packetStructData.EnumValue,
                        new NumberFormatInfo());
                    usedValues.Add(tempVal);
                }

                sw.WriteLine($$"""
                                   [MethodImpl(MethodImplOptions.NoInlining)]
                                   internal static async Task Handle{{packetStructData.EnumValue}}(IServiceProvider serviceProvider, byte[] data,{{enumTypeString}} opcode ,{{packetHandlerData?.PacketStructHandlerData?.SessionFullIdentifier}} session, CancellationToken cancellationToken)
                                   {
                                     var packet{{packetStructData.EnumValue}} = new {{packetStructData.PacketStructFullIdentifier}}();
                                     var handler{{packetStructData.EnumValue}} = ActivatorUtilities.GetServiceOrCreateInstance<{{handlerData.PacketHandlerIdentifier}}>(serviceProvider);
                                     packet{{packetStructData.EnumValue}}.Deserialize(data);
                                     await handler{{packetStructData.EnumValue}}.HandleAsync(packet{{packetStructData.EnumValue}}, session, cancellationToken);
                                   }
                               """);
            }

            sw.WriteLine("}");

            // TODO: allow overriding
            sw.WriteLine($$"""
                           public static class StubHandler {
                           [MethodImpl(MethodImplOptions.NoInlining)]
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

    private static IEnumerable<IntermediateHandlerAndStructTuple?> ToIntermediateTuple(
        (ImmutableArray<IntermediatePacketStructData?> Left, ImmutableArray<IntermediatePacketHandlerData?> Right)
            tuple, CancellationToken _) {
        var (structDatas, handlerDatas) = tuple;

        var matchingData = handlerDatas.Select(handlerData => {
            if (handlerData?.PacketStructHandlerData == null) {
                return null;
            }

            var structData = structDatas
                .FirstOrDefault(sData =>
                    sData != null && sData.PacketStructFullIdentifier.Equals(
                        handlerData.PacketStructHandlerData.PacketStructFullIdentifier, StringComparison.Ordinal));

            if (structData == null) {
                return null;
            }

            var intermediateHandlerAndStructTuple = new IntermediateHandlerAndStructTuple(handlerData, structData);
            return intermediateHandlerAndStructTuple;
        });

        var intermediateHandlerAndStructTuples = matchingData.Where(x => x != null);
        return intermediateHandlerAndStructTuples;
    }

    private IntermediatePacketStructData? TransformPacketStructs(GeneratorSyntaxContext syntaxContext,
        CancellationToken cancellationToken) {
        var structDeclaration = (StructDeclarationSyntax)syntaxContext.Node;
        var model = syntaxContext.SemanticModel;
        var symbol =
            ModelExtensions.GetDeclaredSymbol(model, structDeclaration, cancellationToken) as
                INamedTypeSymbol;
        var requiredInterfaces = new[] { "IPacket", "IIncomingPacket", "IOutgoingPacket", "IBidirectionalPacket" };
        var implementsInterface = symbol != null &&
                                  symbol.AllInterfaces.Any(i =>
                                      requiredInterfaces.Contains(i.Name, StringComparer.Ordinal));
        if (!implementsInterface) {
            // TODO: https://github.com/dotnet/roslyn/blob/main/docs/features/incremental-generators.cookbook.md#issue-diagnostics
            // or Analyzer
            /*var diagnostic = Diagnostic.Create(_rpmGen001Diagnostic, symbol?.Locations.First(),
                            symbol?.ToDisplayString());*/
        }

        // Check for the marker attribute
        var attribute = symbol?.GetAttributes()
            .FirstOrDefault(attr => {
                var attrClass = attr.AttributeClass;
                while (attrClass != null) {
                    if (string.Equals(attrClass.Name, "PacketIdAttribute", StringComparison.Ordinal) &&
                        attrClass.ContainingNamespace != null &&
                        string.Equals(attrClass.ContainingNamespace.ToDisplayString(), "RaiNote.PacketMediator",
                            StringComparison.Ordinal)) {
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
            .OfType<IFieldSymbol>()
            .Max(x => x.ConstantValue);

        if (symbol == null || enumMember == null || enumMaxValue == null || enumType == null ||
            enumValue == null) {
            return null;
        }

        var intermediatePacketStructData = new IntermediatePacketStructData(symbol.Locations.First(),
            symbol.ToDisplayString(), enumValue.ToString(), enumType.ToDisplayString(), enumMember.ToDisplayString(),
            enumMaxValue.ToString(), implementsInterface);

        return intermediatePacketStructData;
    }

    private static IntermediatePacketHandlerData? TransformPacketHandlers(GeneratorSyntaxContext syntaxContext,
        CancellationToken cancellationToken) {
        var classDeclaration = (ClassDeclarationSyntax)syntaxContext.Node;
        var model = syntaxContext.SemanticModel;
        var symbol =
            ModelExtensions.GetDeclaredSymbol(model, classDeclaration, cancellationToken) as
                INamedTypeSymbol;
        var packetStruct = (symbol?.Interfaces.Select(interfaceSyntax => {
            if (!interfaceSyntax.Name.Equals("IPacketHandler", StringComparison.Ordinal)) {
                return null;
            }

            if (interfaceSyntax.TypeArguments.Length != 2) {
                return null;
            }

            var genericStructArgument = interfaceSyntax.TypeArguments[0];
            var genericSessionArgument = interfaceSyntax.TypeArguments[1];

            return new IntermediatePacketStructHandlerData(genericStructArgument.ToDisplayString(),
                genericSessionArgument.ToDisplayString());
        }) ?? throw new InvalidOperationException("1")).FirstOrDefault(x => x != null);

        if (packetStruct == null) {
            return null;
        }

        var intermediatePacketHandlerData = new IntermediatePacketHandlerData(symbol.ToDisplayString(), packetStruct);

        return intermediatePacketHandlerData;
    }
}
