using System;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using DasMulli.Win32.ServiceUtils;
using Microsoft.DotNet.InternalAbstractions;
using System.IO;

namespace Orchard.Cms.Web
{
    public class Program
    {
        public const string RunAsServiceFlag = "--run-as-service";
        private const string RegisterServiceFlag = "--register-service";
        private const string UnregisterServiceFlag = "--unregister-service";
        private const string ServiceNameFlag = "--service-name";
        private const string ServiceDysplayNameFlag = "--service-display-name";
        private const string ServiceDescriptionFlag = "--service-description";
        private const string HelpFlag = "--?";

        private const string DefaultServiceName = "Orchard 2";
        private const string DefaultServiceDisplayName = "Orchard 2";
        private const string DefaultServiceDescription = "Orchard 2 CMS running on .NET Core";

        public static void Main(string[] args)
        {
            try
            {
                if (args.Contains(RunAsServiceFlag))
                {
                    RunAsService(args);
                }
                else if (args.Contains(RegisterServiceFlag))
                {
                    RegisterService();
                }
                else if (args.Contains(UnregisterServiceFlag))
                {
                    UnregisterService();
                }
                else if (args.Contains(HelpFlag))
                {
                    DisplayHelp();
                }
                else
                {
                    RunInteractive(args);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error ocurred: {ex.Message}");
            }
        }

        private static void RunAsService(string[] args)
        {
            var appPath = ApplicationEnvironment.ApplicationBasePath;
            while (!File.Exists(appPath + "\\web.config"))
            {
                appPath = Directory.GetParent(appPath).FullName;
            }

            System.IO.Directory.SetCurrentDirectory(appPath);
            var orchardService = new OrchardCmsWin32Service(commandLineArguments: args.Where(a => a != RunAsServiceFlag).ToArray(), useInteractiveCommandLine: false);
            var serviceHost = new Win32ServiceHost(orchardService);
            serviceHost.Run();
        }

        private static void RunInteractive(string[] args)
        {
            var orchardService = new OrchardCmsWin32Service(commandLineArguments: args, useInteractiveCommandLine: true);
            orchardService.Start(new string[0], () => { });
            orchardService.Stop();
        }

        private static void RegisterService()
        {
            // Environment.GetCommandLineArgs() includes the current DLL from a "dotnet my.dll --register-service" call, which is not passed to Main()
            var remainingArgs = System.Environment.GetCommandLineArgs()
                .Where(arg => arg != RegisterServiceFlag 
                                    && !arg.StartsWith(ServiceNameFlag) 
                                    && !arg.StartsWith(ServiceDysplayNameFlag)
                                    && !arg.StartsWith(ServiceDescriptionFlag))
                .Select(EscapeCommandLineArgument)
                .Append(RunAsServiceFlag);
            
            var host = Process.GetCurrentProcess().MainModule.FileName;
            if (!host.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase))
            {
                // For self-contained apps, skip the dll path
                remainingArgs = remainingArgs.Skip(1);
            }

            string serviceName = GetServiceName();
            string serviceDisplayName = GetServiceDysplayName();
            string serviceDescription = GetServiceDescription();
            var fullServiceCommand = host + " " + string.Join(" ", remainingArgs);
            new Win32ServiceManager()
              .CreateService(serviceName, serviceDisplayName, serviceDescription, fullServiceCommand, new Win32ServiceCredentials("NT SERVICE\\" + serviceName, null), autoStart: true, startImmediately: false, errorSeverity: ErrorSeverity.Normal);



            Console.WriteLine($@"Successfully registered ""{serviceDisplayName}"" (""{serviceDescription}"")");
        }

        private static string GetServiceName()
        {
            var serviceNameArg = System.Environment.GetCommandLineArgs().FirstOrDefault(arg => arg.StartsWith(ServiceNameFlag)) ?? ServiceNameFlag + ":" + DefaultServiceName;
            return serviceNameArg.Substring(ServiceNameFlag.Length + 1);
        }

        private static string GetServiceDysplayName()
        {
            var serviceDisplayNameArg = System.Environment.GetCommandLineArgs().FirstOrDefault(arg => arg.StartsWith(ServiceDysplayNameFlag)) ?? ServiceDysplayNameFlag + ":" + DefaultServiceDisplayName;
            return serviceDisplayNameArg.Substring(ServiceDysplayNameFlag.Length + 1);
        }

        private static string GetServiceDescription()
        {
            var serviceDescriptionArg = System.Environment.GetCommandLineArgs().FirstOrDefault(arg => arg.StartsWith(ServiceDescriptionFlag)) ?? ServiceDescriptionFlag + ":" + DefaultServiceDescription;
            return serviceDescriptionArg.Substring(ServiceDescriptionFlag.Length + 1);
        }

        private static void UnregisterService()
        {
            string serviceName = GetServiceName();
            string serviceDisplayName = GetServiceDysplayName();
            new Win32ServiceManager()
                                    .DeleteService(serviceName);
            Console.WriteLine("Successfully unregistered service");
        }

        private static void DisplayHelp()
        {
            Console.WriteLine(DefaultServiceDescription);
            Console.WriteLine();
            Console.WriteLine("Use one of the following options for running Orchard as a windows service or standalone:");
            Console.WriteLine("  --register-service        Registers and starts this program as a windows service named \"" + DefaultServiceDisplayName + "\"");
            Console.WriteLine("                            All additional arguments will be passed to ASP.NET Core's WebHostBuilder.");
            Console.WriteLine("  --unregister-service      Removes the windows service created by --register-service.");
            Console.WriteLine("  --service-name:<your service name>     Replace default service name");
            Console.WriteLine("  --service-display-name:<your service name>     Replace default service display-name");
            Console.WriteLine("  --service-description:<your service description>     Replace default service description");
        }

        private static string EscapeCommandLineArgument(string arg)
        {
            // http://stackoverflow.com/a/6040946/784387
            arg = Regex.Replace(arg, @"(\\*)" + "\"", @"$1$1\" + "\"");
            arg = "\"" + Regex.Replace(arg, @"(\\+)$", @"$1$1") + "\"";
            return arg;
        }
    }
}