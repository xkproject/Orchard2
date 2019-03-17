using System.Collections.Generic;
using System.Text;
using System.Linq;

namespace OrchardCore.Apis.GraphQL.Client
{
    public class ContentPartBuilder
    {
        private string _contentPartName;

        private Dictionary<string, string> _keysWithValues = new Dictionary<string, string>();
        private List<string> _keys = new List<string>();

        public ContentPartBuilder(string contentPartName)
        {
            _contentPartName = contentPartName;
        }

        public ContentPartBuilder AddField(string name, string value)
        {
            _keysWithValues.Add(name.ToGraphQLStringFormat(), value);

            return this;
        }

        public ContentPartBuilder AddField(string name)
        {
            _keys.Add(name);

            return this;
        }

        internal string Build()
        {
            var sb = new StringBuilder();
            sb.Append($"{_contentPartName}: {{ ");


            for (var i = 0; i < _keysWithValues.Count; i++)
            {
                var item = _keysWithValues.ElementAt(i);
                sb.Append($"{item.Key}: \"{item.Value}\"");

                if (i < (_keysWithValues.Count - 1))
                {
                    sb.Append(" ");
                }
            }

            foreach (var item in _keys)
            {
                sb.Append(item + " ");
            }

            sb.Append(" }");

            return sb.ToString();
        }
    }
}
