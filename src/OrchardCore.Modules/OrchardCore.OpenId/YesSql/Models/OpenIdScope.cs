using OrchardCore.OpenId.Abstractions.Models;

namespace OrchardCore.OpenId.YesSql.Models
{
    public class OpenIdScope : IOpenIdScope
    {
        /// <summary>
        /// Gets or sets the unique identifier
        /// associated with the current scope.
        /// </summary>
        public string ScopeId { get; set; }

        /// <summary>
        /// Gets or sets the public description
        /// associated with the current scope.
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Gets or sets the physical identifier
        /// associated with the current scope.
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// Gets or sets the unique name
        /// associated with the current scope.
        /// </summary>
        public string Name { get; set; }
    }
}
