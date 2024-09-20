using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PrimitiveStaticDataGenerator.Internal
{
    internal static class Util
    {
        internal static void Throw(Exception exception) => throw exception;

        internal static IEnumerable<SyntaxNode> ParentNodes(this SyntaxNode node)
        {
            if (node is null) Throw(new ArgumentNullException(nameof(node)));

            return getParentNodes(node!);

            static IEnumerable<SyntaxNode> getParentNodes(SyntaxNode node)
            {
                SyntaxNode? parent = node.Parent;
                do
                {
                    if (parent is null) yield break;
                    yield return parent;
                    parent = parent.Parent;

                } while (true);
            }
        }

        internal static string ToValidHintName(this string hintName)
        {
            Span<char> chars = hintName.ToCharArray();
           
            for (int i = 0; i < chars.Length; i++)
            {
                ref char c = ref chars[i];

                if (!SyntaxFacts.IsIdentifierPartCharacter(c)
                    && c != '.'
                    && c != ','
                    && c != '-'
                    && c != '_'
                    && c != ' '
                    && c != '('
                    && c != ')'
                    && c != '['
                    && c != ']'
                    && c != '{'
                    && c != '}')
                {
                    c = '_';
                }
            }

            return chars.ToString();
        }

        internal static string GetFullMetadataName(this ISymbol? s)
        {
            if (s == null || IsRootNamespace(s)) return string.Empty;

            var sb = new StringBuilder(s.MetadataName);

            s = s.ContainingSymbol;
            while (!IsRootNamespace(s))
            {
                sb.Insert(0, '.');
                sb.Insert(0, s.OriginalDefinition.MetadataName);
                s = s.ContainingSymbol;
            }

            return sb.ToString();
        }

        internal static bool IsExtendedPartial(this MethodDeclarationSyntax method)
        {
            if (method is null) return false;

            var hasAccessibility = false;
            var isPartial = false;

            for (int i = 0; i < method.Modifiers.Count; ++i)
            {
                SyntaxKind kind = method.Modifiers[i].Kind();

                if (!hasAccessibility && SyntaxFacts.IsAccessibilityModifier(kind))
                {
                    hasAccessibility = true;
                }
                else if(!isPartial && kind == SyntaxKind.PartialKeyword)
                {
                    isPartial = true;
                }

                if (isPartial && hasAccessibility)
                {
                    return true;
                }
            }

            return false;
        }

        internal static T DescendantNodeAt<T>(this SyntaxNode node, int index) where T : SyntaxNode
            => node.DescendantNodes().OfType<T>().ElementAt(index);

        internal static bool IsRootNamespace(this ISymbol symbol)
            => symbol is INamespaceSymbol s && s.IsGlobalNamespace;

        internal static CompilationUnitSyntax ImplementPartial(this MethodDeclarationSyntax method, Func<MethodDeclarationSyntax, MethodDeclarationSyntax> action)
        {
            if (method is null) Throw(new ArgumentNullException(nameof(method)));

            TypeDeclarationSyntax? typeDeclaration = null;

            foreach (var type in method!.ParentNodes().OfType<TypeDeclarationSyntax>().Reverse().Select(t => t.WithAttributeLists(default).WithMembers(default)))
            {
                if (typeDeclaration is null)
                {
                    typeDeclaration = type;
                }
                else
                {
                    typeDeclaration = typeDeclaration.AddMembers(type);
                }
            }
            if (typeDeclaration is null) Throw(new InvalidOperationException());

            CompilationUnitSyntax compilationUnit = SyntaxFactory.CompilationUnit();

            compilationUnit = compilationUnit.AddUsings(method!.SyntaxTree.GetCompilationUnitRoot().Usings.ToArray());

            var @namespace = method.ParentNodes().OfType<NamespaceDeclarationSyntax>().LastOrDefault()?.WithMembers(default);
            
            MethodDeclarationSyntax methodDeclaration = action(method.WithAttributeLists(default).WithSemicolonToken(default));

            if (typeDeclaration!.DescendantNodes().OfType<TypeDeclarationSyntax>().LastOrDefault() is { } lastType)
            {
                typeDeclaration = typeDeclaration.ReplaceNode(lastType, lastType.AddMembers(methodDeclaration));
            }
            else
            {
                typeDeclaration = typeDeclaration.AddMembers(methodDeclaration);
            }

            if (@namespace is null)
            {
                compilationUnit = compilationUnit.AddMembers(typeDeclaration);
            }
            else
            {
                @namespace = @namespace.AddMembers(typeDeclaration);
                compilationUnit = compilationUnit.AddMembers(@namespace);
            }

            return compilationUnit;
        }

        internal static bool IsPublic(this ISymbol symbol)
            => symbol.DeclaredAccessibility == Accessibility.Public;
    }
}
