using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace Underfloor_heating_generator.Revit
{
    internal static class PluginDependencyResolver
    {
        private static AssemblyLoadContext registeredContext;

#pragma warning disable CA2255 // Add-In Manager may execute this command without invoking IExternalApplication.OnStartup.
        [ModuleInitializer]
        internal static void InitializeModule()
        {
            EnsureRegistered();
        }
#pragma warning restore CA2255

        internal static void EnsureRegistered()
        {
            AssemblyLoadContext context = AssemblyLoadContext.GetLoadContext(
                typeof(PluginDependencyResolver).Assembly) ?? AssemblyLoadContext.Default;
            if (ReferenceEquals(registeredContext, context))
                return;

            if (registeredContext != null)
                registeredContext.Resolving -= ResolveDependency;
            registeredContext = context;
            registeredContext.Resolving += ResolveDependency;
        }

        private static Assembly ResolveDependency(AssemblyLoadContext context, AssemblyName name)
        {
            if (!string.Equals(name.Name, "Clipper2Lib", StringComparison.OrdinalIgnoreCase))
                return null;

            string pluginDirectory = Path.GetDirectoryName(
                typeof(PluginDependencyResolver).Assembly.Location);
            string dependencyPath = Path.Combine(pluginDirectory, "Clipper2Lib.dll");
            if (!File.Exists(dependencyPath))
                return null;

            // Revit/AddIn Manager may load the add-in outside the default probing
            // paths. Load the shipped dependency from beside the add-in instead
            // of loading an embedded assembly stream during dependency resolution.
            return context.LoadFromAssemblyPath(dependencyPath);
        }
    }
}
