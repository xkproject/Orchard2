using System;
using System.Collections.Generic;
using GraphQL;
using GraphQL.Resolvers;
using GraphQL.Types;
using OrchardCore.ContentFields.Fields;
using OrchardCore.ContentFields.GraphQL.Types;
using OrchardCore.ContentManagement;
using OrchardCore.ContentManagement.GraphQL.Queries.Types;
using OrchardCore.ContentManagement.Metadata.Models;

namespace OrchardCore.ContentFields.GraphQL.Fields
{
    public class ContentFieldsProvider : IContentFieldProvider
    {
        private static readonly Dictionary<string, FieldTypeDescriptor> ContentFieldTypeMappings = new Dictionary<string, FieldTypeDescriptor>
        {
            {
                nameof(BooleanField),
                new FieldTypeDescriptor
                {
                    Description = "Boolean field",
                    FieldType = typeof(BooleanGraphType),
                    FieldAccessor = field => (bool)field.Content.Value
                }
            },
            {
                nameof(DateField),
                new FieldTypeDescriptor
                {
                    Description = "Date field",
                    FieldType = typeof(DateGraphType),
                    FieldAccessor = field => (DateTime?)field.Content.Value
                }
            },
            {
                nameof(DateTimeField),
                new FieldTypeDescriptor
                {
                    Description = "Date & time field",
                    FieldType = typeof(DateGraphType),
                    FieldAccessor = field => (DateTime?)field.Content.Value
                }
            },
            {
                nameof(HtmlField),
                new FieldTypeDescriptor
                {
                    Description = "Html field",
                    FieldType = typeof(StringGraphType),
                    FieldAccessor = field => field.Content.Html
                }
            },
            {
                nameof(NumericField),
                new FieldTypeDescriptor
                {
                    Description = "Numeric field",
                    FieldType = typeof(DecimalGraphType),
                    FieldAccessor = field => (decimal?)field.Content.Value
                }
            },
            {
                nameof(TextField),
                new FieldTypeDescriptor
                {
                    Description = "Text field",
                    FieldType = typeof(StringGraphType),
                    FieldAccessor = field => field.Content.Text
                }
            },
            {
                nameof(TimeField),
                new FieldTypeDescriptor
                {
                    Description = "Time field",
                    FieldType = typeof(TimeSpanGraphType),
                    FieldAccessor = field => (TimeSpan?)field.Content.Value
                }
            }
        };

        public FieldType GetField(ContentPartFieldDefinition field)
        {
            if (!ContentFieldTypeMappings.ContainsKey(field.FieldDefinition.Name)) return null;

            var fieldDescriptor = ContentFieldTypeMappings[field.FieldDefinition.Name];
            return new FieldType
            {
                Name = field.Name,
                Description = fieldDescriptor.Description,
                Type = fieldDescriptor.FieldType,
                Resolver = new FuncFieldResolver<ContentElement, object>(context =>
                {
                    var contentPart = context.Source.Get(typeof(ContentPart), field.PartDefinition.Name);
                    var contentField = contentPart?.Get(typeof(ContentField), context.FieldName.ToPascalCase());

                    if (contentField == null)
                    {
                        contentField = context.Source.Get(typeof(ContentField), context.FieldName.ToPascalCase());
                    }

                    return contentField == null ? null : fieldDescriptor.FieldAccessor(contentField);
                })
            };
        }

        class FieldTypeDescriptor
        {
            public string Description { get; set; }
            public Type FieldType { get; set; }
            public Func<ContentElement, object> FieldAccessor { get; set; }
        }
    }
}