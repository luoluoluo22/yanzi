using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace OpenQuickHost;

// async void callbacks escape the invocation task and otherwise terminate the shared runtime.
internal static class CSharpAsyncCallbackGuard
{
    internal const string HelperType = "__YanziAsyncCallbackGuard";
    internal const string HelperSource = """
        internal static class __YanziAsyncCallbackGuard
        {
            public static System.Action<System.Exception>? ErrorHandler;
            public static void Report(System.Exception error)
            {
                try
                {
                    if (ErrorHandler is { } handler) handler(error);
                    else System.Diagnostics.Trace.TraceError(error.ToString());
                }
                catch { } // Reporting failures must not become another unhandled callback exception.
            }
        }
        """;

    internal static CSharpCompilation Protect(CSharpCompilation compilation, SyntaxTree source)
    {
        var root = new Rewriter(compilation.GetSemanticModel(source)).Visit(source.GetRoot())!;
        var rewritten = CSharpSyntaxTree.Create((CSharpSyntaxNode)root,
            (CSharpParseOptions)source.Options, source.FilePath, Encoding.UTF8);
        return compilation.ReplaceSyntaxTree(source, rewritten).AddSyntaxTrees(
            CSharpSyntaxTree.ParseText(HelperSource, path: "YanziAsyncCallbackGuard.g.cs", encoding: Encoding.UTF8));
    }

    private sealed class Rewriter(SemanticModel model) : CSharpSyntaxRewriter
    {
        private bool IsVoidDelegate(SyntaxNode node) =>
            model.GetTypeInfo(node).ConvertedType is INamedTypeSymbol type &&
            type.DelegateInvokeMethod?.ReturnsVoid == true;

        private static BlockSyntax Guard(CSharpSyntaxNode body, int position)
        {
            var block = body switch
            {
                BlockSyntax existing => existing,
                ThrowExpressionSyntax thrown => SyntaxFactory.Block(SyntaxFactory.ThrowStatement(thrown.Expression)),
                ExpressionSyntax expression => SyntaxFactory.Block(SyntaxFactory.ExpressionStatement(expression)),
                _ => throw new InvalidOperationException("Unsupported async callback body.")
            };
            var variable = "__yanziCallbackError" + position;
            var handler = SyntaxFactory.CatchClause()
                .WithDeclaration(SyntaxFactory.CatchDeclaration(
                    SyntaxFactory.ParseTypeName("global::System.Exception"), SyntaxFactory.Identifier(variable)))
                .WithBlock(SyntaxFactory.Block(SyntaxFactory.ParseStatement(
                    $"global::{HelperType}.Report({variable});")));
            return SyntaxFactory.Block(SyntaxFactory.TryStatement(block,
                SyntaxFactory.SingletonList(handler), null));
        }

        public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            var protect = node.Modifiers.Any(SyntaxKind.AsyncKeyword) && model.GetDeclaredSymbol(node)?.ReturnsVoid == true;
            var visited = (MethodDeclarationSyntax)base.VisitMethodDeclaration(node)!;
            return protect && (visited.Body is not null || visited.ExpressionBody is not null)
                ? visited.WithBody(Guard((CSharpSyntaxNode?)visited.Body ?? visited.ExpressionBody!.Expression, node.SpanStart))
                    .WithExpressionBody(null).WithSemicolonToken(default)
                : visited;
        }

        public override SyntaxNode? VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
        {
            var protect = node.Modifiers.Any(SyntaxKind.AsyncKeyword) && model.GetDeclaredSymbol(node)?.ReturnsVoid == true;
            var visited = (LocalFunctionStatementSyntax)base.VisitLocalFunctionStatement(node)!;
            return protect && (visited.Body is not null || visited.ExpressionBody is not null)
                ? visited.WithBody(Guard((CSharpSyntaxNode?)visited.Body ?? visited.ExpressionBody!.Expression, node.SpanStart))
                    .WithExpressionBody(null).WithSemicolonToken(default)
                : visited;
        }

        public override SyntaxNode? VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node)
        {
            var protect = node.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword) && IsVoidDelegate(node);
            var visited = (SimpleLambdaExpressionSyntax)base.VisitSimpleLambdaExpression(node)!;
            return protect ? visited.WithBody(Guard(visited.Body, node.SpanStart)) : visited;
        }

        public override SyntaxNode? VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node)
        {
            var protect = node.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword) && IsVoidDelegate(node);
            var visited = (ParenthesizedLambdaExpressionSyntax)base.VisitParenthesizedLambdaExpression(node)!;
            return protect ? visited.WithBody(Guard(visited.Body, node.SpanStart)) : visited;
        }

        public override SyntaxNode? VisitAnonymousMethodExpression(AnonymousMethodExpressionSyntax node)
        {
            var protect = node.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword) && IsVoidDelegate(node);
            var visited = (AnonymousMethodExpressionSyntax)base.VisitAnonymousMethodExpression(node)!;
            return protect ? visited.WithBlock(Guard(visited.Block, node.SpanStart)) : visited;
        }
    }
}
