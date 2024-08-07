using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using JetBrains.Annotations;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace SourceGenerators1;

[Generator]
public class PacketHandlerGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var attributeProvider = context.SyntaxProvider.CreateSyntaxProvider(
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, _) => GetAttributesInheriting(ctx)
            )
            .Where(m => m is not null);

        var attribute = attributeProvider.Select((data, _) => data);

        /*
        var classProvider = context.SyntaxProvider.CreateSyntaxProvider(
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: (ctx, _) => GetIncomingPacketTypes(ctx, attribute)
            )
            .Where(m => m is not null);

        var test = attributeProvider.Combine(classProvider.Collect());*/

        context.RegisterSourceOutput(context.CompilationProvider.Combine(attributeProvider.Collect()),
            Execute
        );
    }

    private static string? GetIncomingPacketTypes(GeneratorSyntaxContext context, string? desiredAttributeName)
    {
        var structDeclarationSyntax = (ClassDeclarationSyntax)context.Node;

        var isValidBaseType = false;
        foreach (var baseTypeSyntax in structDeclarationSyntax.BaseList?.Types ?? Enumerable.Empty<BaseTypeSyntax>())
        {
            var simpleBaseTypeSyntax = baseTypeSyntax as SimpleBaseTypeSyntax;
            var genericName = simpleBaseTypeSyntax?.Type as GenericNameSyntax;
            if (genericName?.Identifier.ToString() !=
                GetAttributeName(typeof(IIncomingPacket)))
            {
                continue;
            }

            isValidBaseType = true;
        }

        if (!isValidBaseType)
        {
            return null;
        }

        foreach (var attributeList in structDeclarationSyntax.AttributeLists)
        {
            foreach (var attributeSyntax in attributeList.Attributes)
            {
                if (attributeSyntax.Name.ToString() != desiredAttributeName)
                    continue;

                return structDeclarationSyntax.Identifier.Text;
            }
        }

        return null;
    }

    private static AttributeClassMapping? GetAttributesInheriting(GeneratorSyntaxContext context)
    {
        var structDeclarationSyntax = (ClassDeclarationSyntax)context.Node;
        var targetName = structDeclarationSyntax.Identifier.Text;
        string? attributeName = null;
        var isValidAttribute = false;
        var isValidPacketType = false;
        foreach (var attributeList in structDeclarationSyntax.AttributeLists)
        {
            foreach (var attributeSyntax in attributeList.Attributes)
            {
            }
        }
        foreach (var baseTypeSyntax in structDeclarationSyntax.BaseList?.Types ?? Enumerable.Empty<BaseTypeSyntax>())
        {
            var simpleBaseTypeSyntax = baseTypeSyntax as SimpleBaseTypeSyntax;
            var genericName = simpleBaseTypeSyntax?.Type as GenericNameSyntax;
            if (genericName?.Identifier.ToString() ==
                GetAttributeName(typeof(PacketIdAttribute<>)))
            {
                isValidAttribute = true;
                attributeName = simpleBaseTypeSyntax?.ToFullString();
            }
            var interfaceName = simpleBaseTypeSyntax?.Type as IdentifierNameSyntax;

            if (interfaceName?.Identifier.ToString() == nameof(IIncomingPacket))
                isValidPacketType = true;
        }

        if (isValidPacketType)
            return new AttributeClassMapping(targetName, attributeName);

        return null;
    }

    private struct AttributeClassMapping
    {
        public AttributeClassMapping(string? className, string? attributeName)
        {
            this.ClassName = className;
            this.AttributeName = attributeName;
        }

        public readonly string? ClassName;
        public readonly string? AttributeName;
    }

    private static string GetAttributeName(Type type)
    {
        return type.IsGenericType ? type.Name.Split('`')[0] : type.Name;
    }

    private static void Execute(SourceProductionContext context,
        (Compilation Left, ImmutableArray<AttributeClassMapping?> Right) tuple)
    {
        var dependencies = new string[] { "System", "System.Threading.Channels", "SourceGenerators1.Sample" };
        /*
         var lambda = CodeGenerator.Lambda<Func<byte[], IIncomingPacket>>(fun =>
            {
                var argPacketData = fun[0];
                var newPacket = packetsType.Value.New();

                var packetVariable = CodeGenerator.DeclareVariable(packetsType.Value, "packet");
                CodeGenerator.Assign(packetVariable, newPacket);
                CodeGenerator.Call(packetVariable, nameof(IIncomingPacket.Deserialize), argPacketData);

                CodeGenerator.Return(packetVariable);
            }).Compile();
         */
        var allparsingFunctions = "";
        context.ReportDiagnostic(Diagnostic.Create(
                new DiagnosticDescriptor("fuckyou0001",
                    "FuckYou",
                    "Message: {0}",
                    "FUCK YOU",
                    DiagnosticSeverity.Error,
                    true
                ),
                Location.None,
                string.Join(", ",
                    tuple.Right.Select(x =>
                        {
                            if (!x.HasValue)
                                return string.Empty;
                            return x.Value.AttributeName + ":" + x.Value.ClassName;
                        }
                    )
                )
            )
        );
        if (tuple.Right.Length == 0)
        {
            throw new Exception();
        }

        foreach (var packetName in tuple.Right)
        {
            var parsingFunctionTemplate = $$"""
                                            public {{packetName?.ClassName}} Parse(byte[] data) {
                                               var packet = new {{packetName?.ClassName}}();
                                               packet.Deserialize(data);
                                               return packet;
                                            }
                                            """;
            allparsingFunctions += parsingFunctionTemplate;
        }

        var codeTemplate = $$"""
                             //<auto-generated/>
                             {{string.Join('\n', dependencies.Select(dep => $"using {dep};"))}}

                             public class PacketDistributorService{

                             {{allparsingFunctions}}

                             private async Task InvokePacketHandlerAsync((byte[], TPacketIdEnum, TSession) valueTuple,
                                 CancellationToken cancellationToken)
                             {
                                 var (packetData, operationCode, session) = valueTuple;
                                 if (!_deserializationMap.TryGetValue(operationCode, out var func))
                                 {
                                     return;
                                 }

                                 var packet = func(packetData);

                                 await _packetHandlersInstantiation[operationCode]?.TryHandleAsync(packet, session, cancellationToken)!;
                             }
                             }
                             """;
        context.AddSource("Test.g.cs", SourceText.From(codeTemplate, Encoding.UTF8));
    }
}

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public abstract class PacketIdAttribute<TPacketIdEnum> : Attribute where TPacketIdEnum : Enum
{
    protected PacketIdAttribute(TPacketIdEnum code)
    {
        Code = code;
    }

    public TPacketIdEnum Code { get; }
}

[UsedImplicitly(ImplicitUseTargetFlags.WithInheritors)]
public interface IIncomingPacket : IPacket
{
    [UsedImplicitly(ImplicitUseTargetFlags.WithInheritors)]
    public void Deserialize(byte[] data);
}

public interface IPacket;
