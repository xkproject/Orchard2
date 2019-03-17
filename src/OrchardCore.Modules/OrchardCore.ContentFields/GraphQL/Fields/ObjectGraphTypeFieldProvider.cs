using System.Linq;
using GraphQL;
using GraphQL.Resolvers;
using GraphQL.Types;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using OrchardCore.ContentManagement;
using OrchardCore.ContentManagement.GraphQL.Queries.Types;
using OrchardCore.ContentManagement.Metadata.Models;

namespace OrchardCore.ContentFields.GraphQL.Fields
{
    public class ObjectGraphTypeFieldProvider : IContentFieldProvider
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public ObjectGraphTypeFieldProvider(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public FieldType GetField(ContentPartFieldDefinition field)
        {
            var serviceProvider = _httpContextAccessor.HttpContext.RequestServices;
            var typeActivator = serviceProvider.GetService<ITypeActivatorFactory<ContentField>>();
            var activator = typeActivator.GetTypeActivator(field.FieldDefinition.Name);

            var queryGraphType = typeof(ObjectGraphType<>).MakeGenericType(activator.Type);

            if (serviceProvider.GetService(queryGraphType) is IObjectGraphType)
            {
                return new FieldType
                {
                    Name = field.Name,
                    Description = field.FieldDefinition.Name,
                    Type = queryGraphType,
                    Resolver = new FuncFieldResolver<ContentElement, ContentElement>(context =>
                    {
                        var typeToResolve = context.ReturnType.GetType().BaseType.GetGenericArguments().First();

                        var contentPart = context.Source.Get(typeof(ContentPart), field.PartDefinition.Name);
                        var contentField = contentPart?.Get(typeToResolve, context.FieldName.ToPascalCase());
                        return contentField;
                    })
                };
            }

            return null;
        }
    }
}