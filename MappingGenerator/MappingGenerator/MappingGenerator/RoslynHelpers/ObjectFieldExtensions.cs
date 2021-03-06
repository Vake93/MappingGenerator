using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace MappingGenerator.RoslynHelpers
{
    public static class ObjectFieldExtensions
    {
        public static IEnumerable<IObjectField> GetObjectFields(this ITypeSymbol type)
        {
            var allMembers = type.GetAllMembers();
            foreach (var objectField in GetObjectFields(allMembers))
                yield return objectField;
        }

        public static IEnumerable<IObjectField> GetObjectFields(IEnumerable<ISymbol> allMembers)
        {
            foreach (var symbol in allMembers)
            {
                if (symbol.IsStatic)
                {
                    continue;
                }

                if (symbol is IPropertySymbol property)
                {
                    if (property.IsIndexer == false)
                    {
                        yield return new ObjectProperty(property);
                    }

                    continue;
                }

                if (symbol is IFieldSymbol fieldSymbol)
                {
                    if (fieldSymbol.IsImplicitlyDeclared == false)
                    {
                        yield return new ObjectField(fieldSymbol);
                    }
                }
            }
        }

       
    } 
}