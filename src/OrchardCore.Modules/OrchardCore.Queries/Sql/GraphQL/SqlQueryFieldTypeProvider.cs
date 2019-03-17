using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GraphQL.Resolvers;
using GraphQL.Types;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OrchardCore.Apis.GraphQL;
using OrchardCore.ContentManagement.GraphQL.Queries;

namespace OrchardCore.Queries.Sql.GraphQL.Queries
{
    /// <summary>
    /// This implementation of <see cref="ISchemaBuilder"/> registers 
    /// all SQL Qeries as GraphQL queries.
    /// </summary>
    public class SqlQueryFieldTypeProvider : ISchemaBuilder
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public SqlQueryFieldTypeProvider(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task<IChangeToken> BuildAsync(ISchema schema)
        {
            var queryManager = _httpContextAccessor.HttpContext.RequestServices.GetService<IQueryManager>();
            var queries = await queryManager.ListQueriesAsync();

            var fieldTypes = new List<FieldType>();

            foreach (var query in queries.OfType<SqlQuery>())
            {
                if (String.IsNullOrWhiteSpace(query.Schema))
                    continue;

                var name = query.Name;
                var source = query.Source;

                var querySchema = JObject.Parse(query.Schema);

                var type = querySchema["type"].ToString();

                if (type.StartsWith("ContentItem/", StringComparison.OrdinalIgnoreCase))
                {
                    var contentType = type.Remove(0, 12);
                    schema.Query.AddField(BuildContentTypeFieldType(schema, contentType, query));
                }
                else
                {
                    schema.Query.AddField(BuildSchemaBasedFieldType(schema, query, querySchema));
                }
            }

            return queryManager.ChangeToken;
        }

        private FieldType BuildSchemaBasedFieldType(ISchema schema, SqlQuery query, JToken querySchema)
        {
            var typetype = new ObjectGraphType<JObject>
            {
                Name = query.Name
            };

            var properties = querySchema["Properties"];

            foreach (var child in properties.Children())
            {
                var name = ((JProperty)child).Name;
                var nameLower = name.Replace('.', '_');
                var type = child["type"].ToString();

                if (type == "String")
                {
                    var field = typetype.Field(
                        typeof(StringGraphType),
                        nameLower,
                        resolve: context =>
                        {
                            var source = context.Source;
                            return source[context.FieldDefinition.Metadata["Name"].ToString()].ToObject<string>();
                        });
                    field.Metadata.Add("Name", name);
                }
                if (type == "Integer")
                {
                    var field = typetype.Field(
                        typeof(IntGraphType),
                        nameLower,
                        resolve: context =>
                        {
                            var source = context.Source;
                            return source[context.FieldDefinition.Metadata["Name"].ToString()].ToObject<int>();
                        });
                    field.Metadata.Add("Name", name);
                }
            }

            var fieldType = new FieldType
            {
                Arguments = new QueryArguments(
                    new QueryArgument<StringGraphType> { Name = "parameters" }
                ),

                Name = query.Name,
                ResolvedType = new ListGraphType(typetype),
                Resolver = new AsyncFieldResolver<object, object>(async context => 
                {
                    var queryManager = _httpContextAccessor.HttpContext.RequestServices.GetService<IQueryManager>();
                    var iquery = await queryManager.GetQueryAsync(context.FieldName);

                    var parameters = context.GetArgument<string>("parameters");

                    var queryParameters = parameters != null ?
                        JsonConvert.DeserializeObject<Dictionary<string, object>>(parameters)
                        : new Dictionary<string, object>();

                    return await queryManager.ExecuteQueryAsync(iquery, queryParameters);
                }),
                Type = typeof(ListGraphType<ObjectGraphType<JObject>>)
            };

            return fieldType;
        }

        private FieldType BuildContentTypeFieldType(ISchema schema, string contentType, SqlQuery query)
        {
            var typetype = schema.Query.Fields.OfType<ContentItemsFieldType>().First(x => x.Name == contentType);

            var fieldType = new FieldType
            {
                Arguments = new QueryArguments(
                    new QueryArgument<StringGraphType> { Name = "parameters" }
                ),

                Name = query.Name,
                ResolvedType = typetype.ResolvedType,
                Resolver = new AsyncFieldResolver<object, object>(async context => 
                {
                    var queryManager = _httpContextAccessor.HttpContext.RequestServices.GetService<IQueryManager>();
                    var iquery = await queryManager.GetQueryAsync(context.FieldName);

                    var parameters = context.GetArgument<string>("parameters");

                    var queryParameters = parameters != null ?
                        JsonConvert.DeserializeObject<Dictionary<string, object>>(parameters)
                        : new Dictionary<string, object>();

                    return await queryManager.ExecuteQueryAsync(iquery, queryParameters);
                }),
                Type = typetype.Type
            };

            return fieldType;
        }
    }
}
