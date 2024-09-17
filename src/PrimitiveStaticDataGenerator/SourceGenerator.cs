using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using PrimitiveStaticDataGenerator.Internal;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace PrimitiveStaticDataGenerator
{
    [Generator]
    public class SourceGenerator : ISourceGenerator
    {
        public void Initialize(GeneratorInitializationContext context)
        {
            context.RegisterForPostInitialization(context =>
            {
                SourceText attributeSourceText = constractSourceText(new PrimitiveStaticDataAttributeTemplate().TransformText());
                context.AddSource(PrimitiveStaticDataAttributeTemplate.TypeFullName, attributeSourceText);
            });

            context.RegisterForSyntaxNotifications(() => new StaticPartialMethodDeclarationSyntaxReceiver());
        }

        public void Execute(GeneratorExecutionContext context)
        {
#if DEBUG
            //if (!Debugger.IsAttached) Debugger.Launch();
#endif
            if (!(context.Compilation is CSharpCompilation compilation)) return;
            if (!(context.ParseOptions is CSharpParseOptions parseOptions)) return;
          
            // check if required types exists.
            if (!(compilation.GetTypeByMetadataName("System.ReadOnlySpan`1") is { } readOnlySpanSymbol)) return;
            if (!(compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.Unsafe") is { } unsafeSymbol)) return;
            if (!(compilation.GetTypeByMetadataName("System.Runtime.InteropServices.MemoryMarshal") is { } memoryMarshalSymbol)) return;

            try
            {
                if (context.SyntaxReceiver is not StaticPartialMethodDeclarationSyntaxReceiver syntaxReceiver)
                    return;

                INamedTypeSymbol attrSymbol = compilation.GetTypeByMetadataName(PrimitiveStaticDataAttributeTemplate.TypeFullName)!;
                foreach (var methodSyntax in syntaxReceiver.Syntaxes)
                {
                    SemanticModel semantic = compilation.GetSemanticModel(methodSyntax.SyntaxTree);

                    if (!(semantic.GetDeclaredSymbol(methodSyntax) is { } methodSymbol)
                        || methodSymbol.MethodKind != MethodKind.Ordinary
                        || methodSymbol.PartialImplementationPart is not null
                        || !equalsSymbol(methodSymbol.ReturnType.OriginalDefinition, readOnlySpanSymbol)) continue;

                    var returnSpanType = (INamedTypeSymbol)methodSymbol.ReturnType;
                    if (!isPrimitiveType(returnSpanType.TypeArguments[0])) continue;

                    foreach (AttributeData attr in methodSymbol.GetReturnTypeAttributes())
                    {
                        if (!equalsSymbol(attr.AttributeClass, attrSymbol)
                            || attr.ConstructorArguments.Length < 1) continue;

                        TypedConstant arg1 = attr.ConstructorArguments[0];

                        bool argIsSingleByteValues = false;
                        IArrayTypeSymbol arraySymbol;
                        object[] values = default!;
                        if(arg1.Kind == TypedConstantKind.Array)
                        {
                            arraySymbol = (IArrayTypeSymbol)arg1.Type!;
                            if (arg1.Values.Length == 0
                                || !arg1.Values.All(v => v.Kind == TypedConstantKind.Primitive)
                                || !equalsSymbol(arraySymbol.ElementType, returnSpanType.TypeArguments[0])) continue;

                            argIsSingleByteValues = arraySymbol.ElementType.SpecialType is SpecialType.System_Boolean or SpecialType.System_SByte or SpecialType.System_Byte;

                            if (argIsSingleByteValues)
                            {
                                values = arg1.Values.Select(v => v.ToCSharpString()).ToArray();
                            }
                            else
                            {
                                values = arg1.Values.Select(v => v.Value!).ToArray();
                            }
                        }
                        else if (arg1.Kind == TypedConstantKind.Primitive 
                                 && equalsSymbol(arg1.Type, compilation.GetSpecialType(SpecialType.System_String))
                                 && returnSpanType.TypeArguments[0].SpecialType is SpecialType.System_Char )
                        {
                            var str = (string?)arg1.Value;
                            if (string.IsNullOrEmpty(str)) continue;

                            arraySymbol = compilation.CreateArrayTypeSymbol(compilation.GetSpecialType(SpecialType.System_Char));

                            values = Array.ConvertAll(str!.ToCharArray(), c => (object)c);
                        }
                        else
                        {
                            continue;
                        }
                  
                        var typeKeyword = arraySymbol.ElementType.ToDisplayString(new SymbolDisplayFormat(
                                          typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameOnly,
                                          miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes));

                        SourceText generatedSource;
                        if (argIsSingleByteValues)
                        {
                            var arrayCreation = (ArrayCreationExpressionSyntax)ParseExpression($"new {typeKeyword}[] {{ {string.Join(", ", values)} }}", options: parseOptions);

                            CompilationUnitSyntax methodImplementation = methodSyntax.ImplementPartial(
                                m => m.WithExpressionBody(ArrowExpressionClause(arrayCreation)).
                                     WithSemicolonToken(Token(SyntaxKind.SemicolonToken)));

                            generatedSource = constractSourceText(methodImplementation.NormalizeWhitespace().ToFullString());
                        }
                        else
                        {
                            const string generateCodeBase =
@"{
    ReadOnlySpan<byte> span;
    if (BitConverter.IsLittleEndian)
    {
        span = new byte[] {};
    }
    else
    {
        span = new byte[] {};
    }
    return default;
}";
                            var block = (BlockSyntax)ParseStatement(generateCodeBase, options: parseOptions);

                            var littleEndianValues = SeparatedList<ExpressionSyntax>(
                                valuesToBytes(arraySymbol.ElementType.SpecialType, values, littleEndian: true).
                                    Select(b => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(b))));

                            block = block.ReplaceNode(block.DescendantNodeAt<ArrayCreationExpressionSyntax>(0).Initializer!,
                                 InitializerExpression(SyntaxKind.ArrayInitializerExpression, littleEndianValues));

                            var bigEndianValues = SeparatedList<ExpressionSyntax>(
                                valuesToBytes(arraySymbol.ElementType.SpecialType, values, littleEndian: false).
                                    Select(b => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(b))));

                            block = block.ReplaceNode(block.DescendantNodeAt<ArrayCreationExpressionSyntax>(1).Initializer!,
                                   InitializerExpression(SyntaxKind.ArrayInitializerExpression, bigEndianValues));

                            // HACK: no good.
                            StatementSyntax @return;
                            if (memoryMarshalSymbol.GetMembers("CreateReadOnlySpan").OfType<IMethodSymbol>().Any())
                            {
                                @return = ParseStatement(
                                    $"return MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<byte, {typeKeyword}>(ref MemoryMarshal.GetReference(span)), {values.Length});", 
                                    options: parseOptions);
                            }
                            else if (compilation.Options.AllowUnsafe)
                            {
                                @return = ParseStatement(
                                    $"unsafe {{ return new ReadOnlySpan<{typeKeyword}>(Unsafe.AsPointer(ref MemoryMarshal.GetReference(span)), {values.Length}); }}",
                                    options: parseOptions);
                            }
                            else if (memoryMarshalSymbol.GetMembers("Cast").OfType<IMethodSymbol>().Where(m => equalsSymbol(m.ReturnType.OriginalDefinition, readOnlySpanSymbol)).Any())
                            {
                                @return = ParseStatement(
                                    $"return MemoryMarshal.Cast<byte, {typeKeyword}>(span);",
                                    options: parseOptions);
                            }
                            else
                            {
                                continue;
                            }

                            block = block.ReplaceNode(block.DescendantNodeAt<ReturnStatementSyntax>(0), @return);

                            CompilationUnitSyntax methodImplementation = methodSyntax.ImplementPartial(m => m.WithBody(block));

                            methodImplementation = methodImplementation.AddUsings(UsingDirective(ParseName(memoryMarshalSymbol.ContainingNamespace.ToDisplayString())));
                            methodImplementation = methodImplementation.AddUsings(UsingDirective(ParseName(unsafeSymbol.ContainingNamespace.ToDisplayString())));

                            generatedSource = constractSourceText(methodImplementation.NormalizeWhitespace().ToFullString());
                        }

                        context.AddSource(methodSymbol.GetFullMetadataName().ToValidHintName(), generatedSource);
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex.ToString());
            }

            static byte[] valuesToBytes(SpecialType primitiveType, object[] values, bool littleEndian)
            {
                int size = getPrimitiveTypeSize(primitiveType);
                var buffer = new byte[size * values.Length];

                for (int i = 0; i < values.Length; ++i)
                {
                    ref var boxValue = ref values[i];
                    var bufferSpan = ((Span<byte>)buffer).Slice(size * i);

                    switch (primitiveType)
                    {
                        case SpecialType.System_Char:
                            {
                                ref var charValue = ref Unsafe.Unbox<char>(boxValue);
                                var value = Unsafe.As<char, ushort>(ref charValue);
                                if (littleEndian)
                                    BinaryPrimitives.WriteUInt16LittleEndian(bufferSpan, value);
                                else
                                    BinaryPrimitives.WriteUInt16BigEndian(bufferSpan, value);
                                break;
                            }
                        case SpecialType.System_UInt16:
                            {
                                ref var value = ref Unsafe.Unbox<ushort>(boxValue);
                                if (littleEndian)
                                    BinaryPrimitives.WriteUInt16LittleEndian(bufferSpan, value);
                                else
                                    BinaryPrimitives.WriteUInt16BigEndian(bufferSpan, value);
                                break;
                            }

                        case SpecialType.System_Int16:
                            {
                                ref var value = ref Unsafe.Unbox<short>(boxValue);
                                if (littleEndian)
                                    BinaryPrimitives.WriteInt16LittleEndian(bufferSpan, value);
                                else
                                    BinaryPrimitives.WriteInt16BigEndian(bufferSpan, value);
                                break;
                            }

                        case SpecialType.System_Int32:
                            {
                                ref var value = ref Unsafe.Unbox<int>(boxValue);
                                if (littleEndian)
                                    BinaryPrimitives.WriteInt32LittleEndian(bufferSpan, value);
                                else
                                    BinaryPrimitives.WriteInt32BigEndian(bufferSpan, value);
                                break;
                            }

                        case SpecialType.System_UInt32:
                            {
                                ref var value = ref Unsafe.Unbox<uint>(boxValue);
                                if (littleEndian)
                                    BinaryPrimitives.WriteUInt32LittleEndian(bufferSpan, value);
                                else
                                    BinaryPrimitives.WriteUInt32BigEndian(bufferSpan, value);
                                break;
                            }

                        case SpecialType.System_Int64:
                            {
                                ref var value = ref Unsafe.Unbox<long>(boxValue);
                                if (littleEndian)
                                    BinaryPrimitives.WriteInt64LittleEndian(bufferSpan, value);
                                else
                                    BinaryPrimitives.WriteInt64BigEndian(bufferSpan, value);
                                break;
                            }

                        case SpecialType.System_UInt64:
                            {
                                ref var value = ref Unsafe.Unbox<ulong>(boxValue);
                                if (littleEndian)
                                    BinaryPrimitives.WriteUInt64LittleEndian(bufferSpan, value);
                                else
                                    BinaryPrimitives.WriteUInt64BigEndian(bufferSpan, value);
                                break;
                            }

                        case SpecialType.System_Single:
                            {
                                ref var floatValue = ref Unsafe.Unbox<float>(boxValue);
                                var value = Unsafe.As<float, int>(ref floatValue);
                                if (littleEndian)
                                    BinaryPrimitives.WriteInt32LittleEndian(bufferSpan, value);
                                else
                                    BinaryPrimitives.WriteInt32BigEndian(bufferSpan, value);
                                break;
                            }

                        case SpecialType.System_Double:
                            {
                                ref var doubleValue = ref Unsafe.Unbox<double>(boxValue);
                                var value = Unsafe.As<double, long>(ref doubleValue);
                                if (littleEndian)
                                    BinaryPrimitives.WriteInt64LittleEndian(bufferSpan, value);
                                else
                                    BinaryPrimitives.WriteInt64BigEndian(bufferSpan, value);
                                break;
                            }

                        default:
                            throw new InvalidOperationException();
                    }
                }

                return buffer;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static bool isPrimitiveType(ITypeSymbol symbol)
            {
                switch (symbol.SpecialType)
                {
                    case SpecialType.System_Boolean:
                    case SpecialType.System_Char:
                    case SpecialType.System_SByte:
                    case SpecialType.System_Byte:
                    case SpecialType.System_Int16:
                    case SpecialType.System_UInt16:
                    case SpecialType.System_Int32:
                    case SpecialType.System_UInt32:
                    case SpecialType.System_Int64:
                    case SpecialType.System_UInt64:
                    case SpecialType.System_Single:
                    case SpecialType.System_Double:
                        return true;
                }
                return false;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static int getPrimitiveTypeSize(SpecialType primitiveType)
            {
                return primitiveType switch
                {
                    SpecialType.System_Boolean => sizeof(bool),
                    SpecialType.System_Char => sizeof(char),
                    SpecialType.System_SByte => sizeof(sbyte),
                    SpecialType.System_Byte => sizeof(byte),
                    SpecialType.System_Int16 => sizeof(short),
                    SpecialType.System_UInt16 => sizeof(ushort),
                    SpecialType.System_Int32 => sizeof(int),
                    SpecialType.System_UInt32 => sizeof(uint),
                    SpecialType.System_Int64 => sizeof(long),
                    SpecialType.System_UInt64 => sizeof(ulong),
                    SpecialType.System_Single => sizeof(float),
                    SpecialType.System_Double => sizeof(double),
                    _ => throw new ArgumentException(nameof(primitiveType)),
                };
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static bool equalsSymbol(ISymbol? symbol1, ISymbol? symbol2)
                => SymbolEqualityComparer.Default.Equals(symbol1, symbol2);


        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static SourceText constractSourceText(string text)
            => SourceText.From(text, Encoding.UTF8);

        private class StaticPartialMethodDeclarationSyntaxReceiver : ISyntaxReceiver
        {
            public List<MethodDeclarationSyntax> Syntaxes { get; } = new();

            public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
            {
                if (syntaxNode is MethodDeclarationSyntax method
                    && method.IsExtendedPartial()
                    && method.Modifiers.Any(SyntaxKind.StaticKeyword)
                    && method.AttributeLists.Count > 0
                    && method.ParentNodes().OfType<TypeDeclarationSyntax>().All(type => type.Modifiers.Any(SyntaxKind.PartialKeyword)))
                {
                    Syntaxes.Add(method);
                }
            }
        }
    }
}
