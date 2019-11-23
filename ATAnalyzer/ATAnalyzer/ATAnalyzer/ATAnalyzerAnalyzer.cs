using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ATAnalyzer
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class ATAnalyzerAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "AssertsAnalyzer";

        // You can change these strings in the Resources.resx file. If you do not want your analyzer to be localize-able, you can use regular strings for Title and MessageFormat.
        // See https://github.com/dotnet/roslyn/blob/master/docs/analyzers/Localizing%20Analyzers.md for more on localization
        private static readonly LocalizableString Title = new LocalizableResourceString(nameof(Resources.AnalyzerTitle), Resources.ResourceManager, typeof(Resources));
        private static readonly LocalizableString MessageFormat = new LocalizableResourceString(nameof(Resources.AnalyzerMessageFormat), Resources.ResourceManager, typeof(Resources));
        private static readonly LocalizableString Description = new LocalizableResourceString(nameof(Resources.AnalyzerDescription), Resources.ResourceManager, typeof(Resources));
        private const string Category = "Syntax";

        private static DiagnosticDescriptor Rule = new DiagnosticDescriptor(DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Warning, isEnabledByDefault: true, description: Description);
        private static DiagnosticDescriptor OverloadRule = new DiagnosticDescriptor(DiagnosticId, "Assertion method without message used, but overloaded method with message exist",
            MessageFormat, Category, DiagnosticSeverity.Warning, isEnabledByDefault: true, description: "There is overloaded method with Message parameter exist and advised to use");

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get { return ImmutableArray.Create(Rule, OverloadRule); } }

        public override void Initialize(AnalysisContext context)
        {
            // TODO: Consider registering other actions that act on syntax instead of or in addition to symbols
            // See https://github.com/dotnet/roslyn/blob/master/docs/analyzers/Analyzer%20Actions%20Semantics.md for more information
            //context.RegisterSymbolAction(AnalyzeSymbol, SymbolKind.NamedType);
            context.RegisterSyntaxNodeAction(AnalyzeNodeAction, SyntaxKind.InvocationExpression);
        }

        private static void AnalyzeNodeAction(SyntaxNodeAnalysisContext context)
        {
            var invocationExpr = (InvocationExpressionSyntax)context.Node;
            var memberAccessExpr = invocationExpr.Expression as MemberAccessExpressionSyntax;
            if (memberAccessExpr == null)
                return;

            // check that method symbol info available
            var memberSymbol =
              context.SemanticModel.GetSymbolInfo(memberAccessExpr).Symbol as IMethodSymbol;
            if (memberSymbol == null)
                return;

            // check that class name has Assert in anme
            if (!memberSymbol?.ContainingType.Name.Equals("Assert") ?? true) return;

            // check that there is method with message parameter
            var methodParameters = memberSymbol.Parameters;
            IParameterSymbol messageParam = null;
            var messageParamIndex = 0;
            if (methodParameters.Any() && methodParameters.Any(x => x.Name == "message"))
            {
                // analyzed method has Message parameter
                messageParam = methodParameters.FirstOrDefault(x => x.Name == "message");
                messageParamIndex = methodParameters.IndexOf(messageParam);
            }
            else
            {
                // if invoked method do not have Message parameter
                // check another methods with same name but with message available
                var methods = memberSymbol?.ContainingType.GetMembers(memberSymbol.Name);
                var checkMethod = methods.Value.Where(x => x is IMethodSymbol).Select(x => x as IMethodSymbol).FirstOrDefault(x => x.Parameters.Any(p => p.Name == "message"));
                // if another method exist add worning to use another method with message available
                if (checkMethod != null)
                {
                    var diagnostic = Diagnostic.Create(OverloadRule, invocationExpr.GetLocation(), memberSymbol.Name);
                    context.ReportDiagnostic(diagnostic);
                }
                return;
            }


            var callEr = context.SemanticModel.GetSymbolInfo(invocationExpr);
            var argumentList = invocationExpr.ArgumentList;
            // if we have 2 arguments in method, and only one provided we for sure do not have message provided
            if (false && (argumentList?.Arguments.Count ?? 0) < 2)
            {
                var diagnostic = Diagnostic.Create(Rule, invocationExpr.GetLocation(), memberSymbol.Name);
                context.ReportDiagnostic(diagnostic);
            }
            else
            {
                // check that message parameter provided as positioned or named parameter
                var arguments = argumentList?.Arguments;
                bool defaultStarted = false;
                int index = 0;
                bool checkSuccess = false;
                foreach (var arg in arguments)
                {

                    var r = context.SemanticModel.GetSymbolInfo(arg.Expression.Parent.Parent.Parent).Symbol;
                    if (arg.NameColon != null)
                    {
                        defaultStarted = true;
                        if (arg.NameColon.Name.Identifier.Value.Equals("message"))
                        {
                            checkSuccess = true;
                        }
                    }
                    if (index == messageParamIndex && !defaultStarted)
                    {
                        checkSuccess = true;
                    }

                    index++;
                }

                if (!checkSuccess)
                {
                    var diagnostic = Diagnostic.Create(Rule, invocationExpr.GetLocation(), memberSymbol.Name);
                    context.ReportDiagnostic(diagnostic);
                }
                // check that we for sure have message provided

            }
        }
    }
}
