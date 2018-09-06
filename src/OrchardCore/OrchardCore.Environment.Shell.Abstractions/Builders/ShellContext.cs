using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using OrchardCore.Environment.Shell;
using OrchardCore.Environment.Shell.Builders.Models;
using OrchardCore.Modules;

namespace OrchardCore.Hosting.ShellBuilders
{
    /// <summary>
    /// The shell context represents the shell's state that is kept alive
    /// for the whole life of the application
    /// </summary>
    public class ShellContext : IDisposable
    {
        private bool _disposed = false;
        private volatile int _refCount = 0;
        private bool _released = false;
        private List<WeakReference<ShellContext>> _dependents;

        public ShellSettings Settings { get; set; }
        public ShellBlueprint Blueprint { get; set; }
        public IServiceProvider ServiceProvider { get; set; }

        /// <summary>
        /// Whether the shell is activated. 
        /// </summary>
        public bool IsActivated { get; set; }

        /// <summary>
        /// Creates a standalone service scope that can be used to resolve local services and
        /// replaces <see cref="HttpContext.RequestServices"/> with it.
        /// </summary>
        /// <remarks>
        /// Disposing the returned <see cref="IServiceScope"/> instance restores the previous state.
        /// </remarks>
        public IServiceScope EnterServiceScope()
        {
            if (_disposed)
            {
                throw new InvalidOperationException("Can't use EnterServiceScope on a disposed context");
            }

            if (_released)
            {
                throw new InvalidOperationException("Can't use EnterServiceScope on a released context");
            }

            return new ServiceScopeWrapper(this);
        }

        /// <summary>
        /// Whether the <see cref="ShellContext"/> instance has been released, for instance when a tenant is changed.
        /// </summary>
        public bool Released => _released;

        /// <summary>
        /// Returns the number of active scopes on this tenant.
        /// </summary>
        public int ActiveScopes => _refCount;

        /// <summary>
        /// Mark the <see cref="ShellContext"/> has a candidate to be released.
        /// </summary>
        public void Release()
        {
            if (_released == true)
            {
                // Prevent infinite loops with circular dependencies
                return;
            }

            // When a tenant is changed and should be restarted, its shell context is replaced with a new one, 
            // so that new request can't use it anymore. However some existing request might still be running and try to 
            // resolve or use its services. We then call this method to count the remaining references and dispose it 
            // when the number reached zero.

            _released = true;

            lock (this)
            {
                if (_dependents == null)
                {
                    return;
                }

                foreach (var dependent in _dependents)
                {
                    if (dependent.TryGetTarget(out var shellContext))
                    {
                        shellContext.Release();
                    }
                }

                // A ShellContext is usually disposed when the last scope is disposed, but if there are no scopes
                // then we need to dispose it right away
                if (_refCount == 0)
                {
                    Dispose();
                }
            }
        }

        /// <summary>
        /// Registers the specified shellContext as a dependency such that they are also reloaded when the current shell context is reloaded.
        /// </summary>
        public void AddDependentShell(ShellContext shellContext)
        {
            lock (this)
            {
                if (_dependents == null)
                {
                    _dependents = new List<WeakReference<ShellContext>>();
                }

                // Remove any previous instance that represent the same tenant in case it has been released (restarted).
                _dependents.RemoveAll(x => !x.TryGetTarget(out var shell) || shell.Settings.Name == shellContext.Settings.Name);

                // The same item can safely be added multiple times in a Hashset
                _dependents.Add(new WeakReference<ShellContext>(shellContext));
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            // Disposes all the services registered for this shell
            if (ServiceProvider != null)
            {
                (ServiceProvider as IDisposable)?.Dispose();
                ServiceProvider = null;
            }

            IsActivated = false;
            Settings = null;
            Blueprint = null;

            _disposed = true;

            GC.SuppressFinalize(this);
        }

        ~ShellContext()
        {
            Dispose();
        }

        internal class ServiceScopeWrapper : IServiceScope
        {
            private readonly ShellContext _shellContext;
            private readonly IServiceScope _serviceScope;
            private readonly IServiceProvider _existingServices;
            private readonly HttpContext _httpContext;

            public ServiceScopeWrapper(ShellContext shellContext)
            {
                // Prevent the context from being released until the end of the scope
                Interlocked.Increment(ref shellContext._refCount);

                _shellContext = shellContext;
                _serviceScope = shellContext.ServiceProvider.CreateScope();
                ServiceProvider = _serviceScope.ServiceProvider;

                var httpContextAccessor = ServiceProvider.GetRequiredService<IHttpContextAccessor>();

                if (httpContextAccessor.HttpContext == null)
                {
                    httpContextAccessor.HttpContext = new DefaultHttpContext();
                }

                _httpContext = httpContextAccessor.HttpContext;
                _existingServices = _httpContext.RequestServices;
                _httpContext.RequestServices = ServiceProvider;
            }

            public IServiceProvider ServiceProvider { get; }

            /// <summary>
            /// Returns true is the shell context should be disposed consequently to this scope being released.
            /// </summary>
            private bool ScopeReleased()
            {
                var refCount = Interlocked.Decrement(ref _shellContext._refCount);

                if (_shellContext._released && refCount == 0)
                {
                    var tenantEvents = _serviceScope.ServiceProvider.GetServices<IModularTenantEvents>();

                    foreach (var tenantEvent in tenantEvents)
                    {
                        tenantEvent.TerminatingAsync().GetAwaiter().GetResult();
                    }

                    foreach (var tenantEvent in tenantEvents.Reverse())
                    {
                        tenantEvent.TerminatedAsync().GetAwaiter().GetResult();
                    }

                    return true;
                }

                return false;
            }

            public void Dispose()
            {
                var disposeShellContext = ScopeReleased();

                _httpContext.RequestServices = _existingServices;
                _serviceScope.Dispose();
                
                GC.SuppressFinalize(this);

                if (disposeShellContext)
                {
                    _shellContext.Dispose();
                }
            }

            ~ServiceScopeWrapper()
            {
                Dispose();
            }
        }
    }
}
