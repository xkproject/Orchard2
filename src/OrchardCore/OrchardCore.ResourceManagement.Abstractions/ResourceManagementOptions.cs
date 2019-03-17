namespace OrchardCore.ResourceManagement
{
    public class ResourceManagementOptions
    {
        public bool UseCdn { get; set; } = true;

        public bool DebugMode { get; set; } = false;

        public string Culture { get; set; }
    }
}
