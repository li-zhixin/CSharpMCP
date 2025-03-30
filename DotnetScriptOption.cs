using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Scripting;

namespace CSharpMCP;

public class DotnetScriptOption
{
    public ScriptOptions Options { get; set; }

    public DotnetScriptOption(string dllPath)
    {
        IEnumerable<MetadataReference> allReferences;

        allReferences = GetDefaultReferences();
        if (!string.IsNullOrEmpty(dllPath))
        {
            allReferences = ResolveDependencies(dllPath, null);
        }

        Options = ScriptOptions.Default
            .WithReferences(allReferences)
            .WithImports(GetRequiredImports());
    }

    private static List<MetadataReference> GetDefaultReferences()
    {
        var references = new List<MetadataReference>();
        var requiredAssemblies = new Dictionary<Type, string>
        {
            { typeof(object), "mscorlib" },
            { typeof(Enumerable), "System.Linq" },
        };

        foreach (var assembly in requiredAssemblies)
        {
            try
            {
                references.Add(MetadataReference.CreateFromFile(assembly.Key.Assembly.Location));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not load {assembly.Value}: {ex.Message}");
            }
        }

        try
        {
            references.AddRange(new[]
            {
                MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
                MetadataReference.CreateFromFile(Assembly.Load("System.Core").Location),
                MetadataReference.CreateFromFile(Assembly.Load("System.Console").Location),
                MetadataReference.CreateFromFile(Assembly.Load("System.Linq.Expressions").Location),
                MetadataReference.CreateFromFile(Assembly.Load("netstandard").Location),
                MetadataReference.CreateFromFile(typeof(Task).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Runtime.CompilerServices.DynamicAttribute).Assembly
                    .Location),
                MetadataReference.CreateFromFile(typeof(Console).Assembly.Location)
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Could not load some core references: {ex.Message}");
        }

        return references;
    }

    private IEnumerable<MetadataReference> ResolveDependencies(string entryDllPath,
        IEnumerable<string>? additionalSearchPaths)
    {
        var resolvedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var assembliesToAnalyze = new Queue<string>();

        // --- Determine Search Paths ---
        var searchPaths = new List<string>();
        var entryDirectory = Path.GetDirectoryName(entryDllPath);
        if (!string.IsNullOrEmpty(entryDirectory))
        {
            searchPaths.Add(entryDirectory); // Primary search location
        }

        // Add runtime directory - often essential for resolving framework assemblies correctly
        searchPaths.Add(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory());
        // Add any extra specified paths
        if (additionalSearchPaths != null)
        {
            searchPaths.AddRange(additionalSearchPaths.Where(p => !string.IsNullOrEmpty(p) && Directory.Exists(p)));
        }

        // Ensure no duplicates
        searchPaths = searchPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        Console.WriteLine("Dependency Search Paths:");
        foreach (var p in searchPaths) Console.WriteLine($"- {p}");

        // --- Create Resolver and Context ---
        // PathAssemblyResolver requires full paths to *potential* dependency files it knows about.
        // It's often simpler to implement a custom resolver or rely on MetadataLoadContext's built-in logic
        // combined with providing the search directories. Let's use a slightly more robust resolver.
        var resolver = new CustomMetadataAssemblyResolver(searchPaths);
        using var context = new MetadataLoadContext(resolver);

        // --- Start Analysis ---
        assembliesToAnalyze.Enqueue(entryDllPath);
        resolvedPaths.Add(entryDllPath); // Add the entry assembly path

        while (assembliesToAnalyze.Count > 0)
        {
            var currentPath = assembliesToAnalyze.Dequeue();

            Assembly? assembly = null;
            try
            {
                Console.WriteLine($"Analyzing: {currentPath}");
                assembly = context.LoadFromAssemblyPath(currentPath);
            }
            catch (FileNotFoundException)
            {
                Console.WriteLine($"Warning: File not found during load: {currentPath}. Skipping.");
                resolvedPaths.Remove(currentPath); // Remove if it couldn't be loaded
                continue; // Skip processing dependencies of a non-loadable assembly
            }
            catch (BadImageFormatException)
            {
                Console.WriteLine($"Warning: Bad image format: {currentPath}. Skipping.");
                resolvedPaths.Remove(currentPath);
                continue;
            }
            catch (Exception ex) // Catch other potential load errors
            {
                Console.WriteLine($"Warning: Error loading assembly {currentPath}: {ex.Message}. Skipping.");
                resolvedPaths.Remove(currentPath);
                continue;
            }

            if (assembly == null) continue; // Should not happen if no exception, but good practice

            // --- Analyze Dependencies ---
            foreach (var assemblyName in assembly.GetReferencedAssemblies())
            {
                // Core framework assemblies like System.Private.CoreLib are often implicitly
                // handled by the scripting host or ScriptOptions.Default. Explicitly adding
                // them can sometimes cause conflicts. You might need to refine this list.
                if (IsCoreFrameworkAssembly(assemblyName.Name))
                {
                    // Console.WriteLine($"Skipping core framework assembly: {assemblyName.Name}");
                    continue;
                }

                try
                {
                    // Attempt to resolve using the context's resolver
                    var referencedAssembly = context.LoadFromAssemblyName(assemblyName);

                    if (referencedAssembly != null && !string.IsNullOrEmpty(referencedAssembly.Location))
                    {
                        // Check if we haven't already added this specific file path
                        if (resolvedPaths.Add(referencedAssembly.Location))
                        {
                            Console.WriteLine(
                                $"Resolved dependency: {assemblyName.Name} -> {referencedAssembly.Location}");
                            assembliesToAnalyze.Enqueue(referencedAssembly.Location);
                        }
                    }
                    else
                    {
                        // This happens if the resolver couldn't find the assembly
                        Console.WriteLine($"Warning: Could not resolve dependency assembly: {assemblyName.FullName}");
                    }
                }
                catch (FileNotFoundException)
                {
                    Console.WriteLine($"Warning: Could not find file for dependency: {assemblyName.FullName}");
                }
                catch (Exception ex) // Catch errors during dependency resolution
                {
                    Console.WriteLine($"Warning: Error resolving dependency {assemblyName.FullName}: {ex.Message}");
                }
            }
        }

        // --- Create MetadataReferences ---
        var metadataReferences = new List<MetadataReference>();
        foreach (var path in resolvedPaths)
        {
            try
            {
                Console.WriteLine($"Adding reference: {path}");
                metadataReferences.Add(MetadataReference.CreateFromFile(path));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Failed to create MetadataReference for {path}: {ex.Message}");
            }
        }

        return metadataReferences;
    }

    // Basic check for core framework assemblies often handled implicitly.
    // This might need adjustment based on your target framework (.NET Framework vs .NET Core/5+).
    private static bool IsCoreFrameworkAssembly(string? assemblyName)
    {
        if (string.IsNullOrEmpty(assemblyName)) return false;
        return assemblyName.Equals("System.Private.CoreLib", StringComparison.OrdinalIgnoreCase) ||
               assemblyName.Equals("mscorlib", StringComparison.OrdinalIgnoreCase) ||
               assemblyName.Equals("netstandard", StringComparison.OrdinalIgnoreCase) ||
               // Add others if needed, e.g., specific System.* assemblies known to be implicit
               assemblyName.StartsWith("System.Runtime.", StringComparison.OrdinalIgnoreCase);
    }


    private string[] GetRequiredImports()
    {
        return
        [
            "System",
            "System.Linq",
            "System.Console",
            "System.Collections",
            "System.Collections.Generic",
            "System.Threading",
            "System.Threading.Tasks",
        ];
    }

    private class CustomMetadataAssemblyResolver(List<string> searchPaths) : MetadataAssemblyResolver
    {
        private readonly List<string>
            _searchPaths = searchPaths ?? throw new ArgumentNullException(nameof(searchPaths));

        private readonly Dictionary<string, Assembly?> _resolvedAssemblies = new(StringComparer.OrdinalIgnoreCase);

        public override Assembly? Resolve(MetadataLoadContext context, AssemblyName assemblyName)
        {
            if (assemblyName == null) return null;

            // Check cache first
            if (_resolvedAssemblies.TryGetValue(assemblyName.FullName, out var cachedAssembly))
            {
                return cachedAssembly; // Return cached result (could be null if previously failed)
            }

            // Try to find the assembly in search paths
            var foundPath = FindAssemblyFile(assemblyName);

            Assembly? resolved = null;
            if (foundPath != null)
            {
                try
                {
                    resolved = context.LoadFromAssemblyPath(foundPath);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        $"Resolver Warning: Failed to load '{foundPath}' for '{assemblyName.FullName}': {ex.Message}");
                    // Don't throw, let the caller handle load failures.
                }
            }
            else
            {
                // Console.WriteLine($"Resolver: Could not find file for '{assemblyName.FullName}' in search paths.");
            }


            // Cache the result (even if null) before returning
            _resolvedAssemblies[assemblyName.FullName] = resolved;
            return resolved;
        }

        private string? FindAssemblyFile(AssemblyName assemblyName)
        {
            if (assemblyName.Name == null) return null;

            string[] fileExtensions = { ".dll", ".exe" }; // Assemblies can be .exe too

            foreach (var dir in _searchPaths)
            {
                foreach (var ext in fileExtensions)
                {
                    // Check for assemblyName.dll in the directory
                    var potentialPath = Path.Combine(dir, assemblyName.Name + ext);
                    if (File.Exists(potentialPath))
                    {
                        // Optional: Verify file matches AssemblyName (version, public key token)
                        // For simplicity here, we'll just return the first match by name.
                        // A production resolver might use context.LoadFromAssemblyPath within a try-catch
                        // here to *validate* the found file matches the requested AssemblyName before returning path.
                        return potentialPath;
                    }
                }

                // Sometimes assemblies are in subdirectories named after the assembly
                var potentialDir = Path.Combine(dir, assemblyName.Name);
                if (Directory.Exists(potentialDir))
                {
                    foreach (var ext in fileExtensions)
                    {
                        var potentialPath = Path.Combine(potentialDir, assemblyName.Name + ext);
                        if (File.Exists(potentialPath))
                        {
                            return potentialPath;
                        }
                    }
                }
            }

            return null; // Not found
        }
    }
}