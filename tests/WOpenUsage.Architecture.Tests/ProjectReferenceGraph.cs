using System.Xml.Linq;

namespace WOpenUsage.Architecture.Tests;

public sealed class ProjectReferenceGraph
{
    private readonly Dictionary<string, HashSet<string>> _edges;

    public ProjectReferenceGraph(
        IEnumerable<string> projects,
        IEnumerable<(string From, string To)> edges)
    {
        _edges = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (string project in projects)
        {
            _edges.TryAdd(project, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }

        foreach ((string from, string to) in edges)
        {
            if (!_edges.TryGetValue(from, out HashSet<string>? references))
            {
                references = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _edges[from] = references;
            }

            _edges.TryAdd(to, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            references.Add(to);
        }
    }

    public IReadOnlyCollection<string> Projects => _edges.Keys.ToArray();

    public IReadOnlyCollection<string> GetReferences(string project) =>
        _edges.TryGetValue(project, out HashSet<string>? references)
            ? references.ToArray()
            : Array.Empty<string>();

    public static ProjectReferenceGraph LoadProductProjects(string repoRoot)
    {
        string sourceRoot = Path.Combine(repoRoot, "src");
        string[] projectPaths = Directory
            .EnumerateFiles(sourceRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !ContainsDirectory(path, "bin") && !ContainsDirectory(path, "obj"))
            .ToArray();

        var edges = new List<(string From, string To)>();

        foreach (string projectPath in projectPaths)
        {
            string from = Path.GetFileNameWithoutExtension(projectPath);
            XDocument project = XDocument.Load(projectPath);

            foreach (XElement reference in project.Descendants("ProjectReference"))
            {
                string? include = (string?)reference.Attribute("Include");
                if (string.IsNullOrWhiteSpace(include))
                {
                    continue;
                }

                string normalized = include.Replace('\\', Path.DirectorySeparatorChar);
                edges.Add((from, Path.GetFileNameWithoutExtension(normalized)));
            }
        }

        return new ProjectReferenceGraph(
            projectPaths.Select(path => Path.GetFileNameWithoutExtension(path)!),
            edges);
    }

    public static string FindRepoRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "WOpenUsage.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Could not locate WOpenUsage.slnx from the test output directory.");
    }

    private static bool ContainsDirectory(string path, string directoryName)
    {
        string marker = $"{Path.DirectorySeparatorChar}{directoryName}{Path.DirectorySeparatorChar}";
        return path.Contains(marker, StringComparison.OrdinalIgnoreCase);
    }
}
