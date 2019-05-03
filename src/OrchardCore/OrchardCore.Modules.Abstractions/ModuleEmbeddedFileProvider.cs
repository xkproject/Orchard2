using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.FileProviders.Internal;
using Microsoft.Extensions.FileProviders.Physical;
using Microsoft.Extensions.Primitives;
using OrchardCore.Modules.FileProviders;

namespace OrchardCore.Modules
{
    /// <summary>
    /// This custom <see cref="IFileProvider"/> implementation provides the file contents
    /// of embedded files in Module assemblies.
    /// </summary>
    public class ModuleEmbeddedFileProvider : IFileProvider
    {
        private readonly IApplicationContext _applicationContext;

        public ModuleEmbeddedFileProvider(IApplicationContext applicationContext)
        {
            _applicationContext = applicationContext;
        }

        private Application Application => _applicationContext.Application;

        public IDirectoryContents GetDirectoryContents(string subpath)
        {
            if (subpath == null)
            {
                return NotFoundDirectoryContents.Singleton;
            }

            var folder = NormalizePath(subpath);

            var entries = new List<IFileInfo>();

            if (folder == "")
            {
                entries.Add(new EmbeddedDirectoryInfo(Application.ModulesPath));
            }
            else if (folder == Application.ModulesPath)
            {
                entries.AddRange(Application.Modules
                    .Select(n => new EmbeddedDirectoryInfo(n.Name)));
            }
            else if (folder == Application.ModulePath)
            {
                return new PhysicalDirectoryContents(Application.Path);
            }
            else if (folder.StartsWith(Application.ModuleRoot, StringComparison.Ordinal))
            {
                var tokenizer = new StringTokenizer(folder, new char[] { '/' });
                if (tokenizer.Any(s => s == "Pages" || s == "Views"))
                {
                    var folderSubPath = folder.Substring(Application.ModuleRoot.Length);
                    return new PhysicalDirectoryContents(Application.Root + folderSubPath);
                }
            }
            else if (folder.StartsWith(Application.ModulesRoot, StringComparison.Ordinal))
            {
                var path = folder.Substring(Application.ModulesRoot.Length);
                var index = path.IndexOf('/');
                var name = index == -1 ? path : path.Substring(0, index);
                var assetPaths = Application.GetModule(name).AssetPaths;
                var folders = new HashSet<string>(StringComparer.Ordinal);
                var folderSlash = folder + '/';

                foreach (var assetPath in assetPaths.Where(a => a.StartsWith(folderSlash, StringComparison.Ordinal)))
                {
                    var folderPath = assetPath.Substring(folderSlash.Length);
                    var pathIndex = folderPath.IndexOf('/');
                    var isFilePath = pathIndex == -1;

                    if (isFilePath)
                    {
                        entries.Add(GetFileInfo(assetPath));
                    }
                    else
                    {
                        folders.Add(folderPath.Substring(0, pathIndex));
                    }
                }

                entries.AddRange(folders.Select(f => new EmbeddedDirectoryInfo(f)));
            }

            return new EmbeddedDirectoryContents(entries);
        }

        public IFileInfo GetFileInfo(string subpath)
        {
            if (subpath == null)
            {
                return new NotFoundFileInfo(subpath);
            }

            var path = NormalizePath(subpath);

            if (path.StartsWith(Application.ModuleRoot, StringComparison.Ordinal))
            {
                var fileSubPath = path.Substring(Application.ModuleRoot.Length);
                return new PhysicalFileInfo(new FileInfo(Application.Root + fileSubPath));
            }
            else if (path.StartsWith(Application.ModulesRoot, StringComparison.Ordinal))
            {
                path = path.Substring(Application.ModulesRoot.Length);
                var index = path.IndexOf('/');

                if (index != -1)
                {
                    var module = path.Substring(0, index);
                    var fileSubPath = path.Substring(index + 1);
                    return Application.GetModule(module).GetFileInfo(fileSubPath);
                }
            }

            return new NotFoundFileInfo(subpath);
        }

        public IChangeToken Watch(string filter)
        {

            if (filter == null)
            {
                return NullChangeToken.Singleton;
            }

            var path = NormalizePath(filter);

            if (path.StartsWith(Application.ModuleRoot, StringComparison.Ordinal))
            {
                var fileSubPath = path.Substring(Application.ModuleRoot.Length);
                return new PollingFileChangeToken(new FileInfo(Application.Root + fileSubPath));
            }

            return NullChangeToken.Singleton;
        }

        private string NormalizePath(string path)
        {
            return path.Replace('\\', '/').Trim('/').Replace("//", "/");
        }
    }
}