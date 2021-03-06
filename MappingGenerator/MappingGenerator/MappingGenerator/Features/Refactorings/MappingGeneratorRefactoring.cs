using System.Collections.Immutable;
using System.Composition;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MappingGenerator.Mappings;
using MappingGenerator.MethodHelpers;
using MappingGenerator.RoslynHelpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Formatting;

namespace MappingGenerator.Features.Refactorings
{
    [ExportCodeRefactoringProvider(LanguageNames.CSharp, Name = nameof(MappingGeneratorRefactoring)), Shared]
    public class MappingGeneratorRefactoring : CodeRefactoringProvider
    {
        private const string GenerateWholeMappingTitle = "Generate mapping code";
        private const string GenerateMappingWithMembersTitle = "Generate mapping code using member functions";

        private static readonly MappingImplementorEngine MappingImplementorEngine = new MappingImplementorEngine();

        public sealed override async Task ComputeRefactoringsAsync(CodeRefactoringContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
            var node = root.FindNode(context.Span);

            await TryToRegisterRefactoring(context, node).ConfigureAwait(false);
        }

        private async Task TryToRegisterRefactoring(CodeRefactoringContext context, SyntaxNode node)
        {
            switch (node)
            {
                case BaseMethodDeclarationSyntax methodDeclaration when IsMappingMethodCandidate(methodDeclaration):
                    if (methodDeclaration.Parent?.Kind() != SyntaxKind.InterfaceDeclaration )
                    {
                        var semanticModel = await context.Document.GetSemanticModelAsync().ConfigureAwait(false);
                        var methodSymbol = semanticModel.GetDeclaredSymbol(methodDeclaration);


                        if (MappingImplementorEngine.CanProvideMappingImplementationFor(methodSymbol))
                        {
                           
                            context.RegisterRefactoring(CodeAction.Create(title: GenerateWholeMappingTitle, createChangedDocument: async (c) => await GenerateMappingMethodBody(context.Document, methodDeclaration, false, c).ConfigureAwait(false), equivalenceKey: GenerateWholeMappingTitle));
                            context.RegisterRefactoring(CodeAction.Create(title: GenerateMappingWithMembersTitle, createChangedDocument: async (c) => await GenerateMappingMethodBody(context.Document, methodDeclaration, true, c).ConfigureAwait(false), equivalenceKey: GenerateMappingWithMembersTitle));
                        }
                    }
                    break;
                case IdentifierNameSyntax _:
                case ParameterListSyntax _:
                   await TryToRegisterRefactoring(context, node.Parent).ConfigureAwait(false);
                break;
            }
        }

        private static bool IsMappingMethodCandidate(BaseMethodDeclarationSyntax methodDeclaration)
        {
            if (methodDeclaration is MethodDeclarationSyntax)
            {
                return true;
            }

            if (methodDeclaration is ConstructorDeclarationSyntax constructorDeclaration && constructorDeclaration.ParameterList.Parameters.Count >= 1)
            {
                return true;
            }

            return false;
        }

        private async Task<Document> GenerateMappingMethodBody(Document document, BaseMethodDeclarationSyntax methodSyntax, bool useMembersMappers, CancellationToken cancellationToken)
        {
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            var methodSymbol = semanticModel.GetDeclaredSymbol(methodSyntax, cancellationToken);
            var generator = SyntaxGenerator.GetGenerator(document);
            var mappingContext = new MappingContext(methodSyntax, semanticModel);
            var accessibilityHelper = new AccessibilityHelper(methodSymbol.ContainingType);


            if (useMembersMappers)
            {
                foreach (var userDefinedConversion in CustomConversionHelper.FindCustomConversionMethods(methodSymbol))
                {
                    if (userDefinedConversion == methodSymbol || accessibilityHelper.IsSymbolAccessible(userDefinedConversion, methodSymbol.ContainingType) == false)
                    {
                        continue;
                    }

                    mappingContext.CustomConversions.Add(new CustomConversion()
                    {
                        FromType = new AnnotatedType(userDefinedConversion.Parameters.First().Type),
                        ToType = new AnnotatedType(userDefinedConversion.ReturnType),
                        Conversion = SyntaxFactory.IdentifierName(userDefinedConversion.Name)
                    });
                }
            }
            var blockSyntax = await MappingImplementorEngine.GenerateMappingBlockAsync(methodSymbol, generator, semanticModel, mappingContext).ConfigureAwait(false);
            return await document.ReplaceNodes(methodSyntax, methodSyntax.WithOnlyBody(blockSyntax), cancellationToken).ConfigureAwait(false);
        }
    }
}
