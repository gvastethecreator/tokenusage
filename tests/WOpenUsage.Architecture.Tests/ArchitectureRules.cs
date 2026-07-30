using System.Xml.Linq;

namespace WOpenUsage.Architecture.Tests;

public static class ArchitectureRules
{
    public static IReadOnlyDictionary<string, IReadOnlySet<string>> AllowedReferences { get; } =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["WOpenUsage.Core"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            ["WOpenUsage.Providers"] = CreateSet("WOpenUsage.Core"),
            ["WOpenUsage.Platform.Windows"] = CreateSet("WOpenUsage.Core"),
            ["WOpenUsage.Runtime.Windows"] = CreateSet(
                "WOpenUsage.Core",
                "WOpenUsage.Providers",
                "WOpenUsage.Platform.Windows"),
            ["WOpenUsage.App"] = CreateSet(
                "WOpenUsage.Core",
                "WOpenUsage.Providers",
                "WOpenUsage.Platform.Windows",
                "WOpenUsage.Runtime.Windows"),
            ["WOpenUsage.Cli"] = CreateSet(
                "WOpenUsage.Core",
                "WOpenUsage.Providers",
                "WOpenUsage.Runtime.Windows"),
            ["WOpenUsage.LocalApi"] = CreateSet(
                "WOpenUsage.Core",
                "WOpenUsage.Runtime.Windows"),
        };

    private static readonly IReadOnlySet<string> OptionalProjects = CreateSet(
        "WOpenUsage.LocalApi");

    public static IReadOnlyList<string> FindForbiddenEdges(ProjectReferenceGraph graph)
    {
        var violations = new List<string>();

        foreach (string missing in AllowedReferences.Keys.Except(
                     graph.Projects,
                     StringComparer.OrdinalIgnoreCase).Except(
                     OptionalProjects,
                     StringComparer.OrdinalIgnoreCase))
        {
            violations.Add($"Missing product project: {missing}");
        }

        foreach (string project in graph.Projects)
        {
            if (!AllowedReferences.TryGetValue(project, out IReadOnlySet<string>? allowed))
            {
                violations.Add($"Unknown product project: {project}");
                continue;
            }

            foreach (string reference in graph.GetReferences(project))
            {
                if (!allowed.Contains(reference))
                {
                    violations.Add($"{project} -> {reference}");
                }
            }
        }

        violations.Sort(StringComparer.OrdinalIgnoreCase);
        return violations;
    }

    public static IReadOnlyList<string> FindCoreIsolationViolations(string coreProjectPath)
    {
        var violations = new List<string>();
        XDocument project = XDocument.Load(coreProjectPath);

        string? targetFramework = project
            .Descendants("TargetFramework")
            .Select(element => element.Value.Trim())
            .FirstOrDefault();

        if (!string.Equals(targetFramework, "net10.0", StringComparison.OrdinalIgnoreCase))
        {
            violations.Add($"Core TargetFramework must be net10.0; found '{targetFramework}'.");
        }

        foreach (XElement reference in project.Descendants("ProjectReference"))
        {
            violations.Add($"Core must not reference a project: {(string?)reference.Attribute("Include")}");
        }

        string[] bannedPackages =
        [
            "Microsoft.WindowsAppSDK",
            "Microsoft.WinUI",
            "Microsoft.Windows.SDK.BuildTools",
            "CommunityToolkit.WinUI",
        ];

        foreach (XElement reference in project.Descendants("PackageReference"))
        {
            string packageId = (string?)reference.Attribute("Include") ?? string.Empty;
            if (bannedPackages.Any(banned => packageId.Contains(banned, StringComparison.OrdinalIgnoreCase)))
            {
                violations.Add($"Core must not reference a UI or Windows package: {packageId}");
            }
        }

        return violations;
    }

    private static HashSet<string> CreateSet(params string[] values) =>
        new HashSet<string>(values, StringComparer.OrdinalIgnoreCase);
}
