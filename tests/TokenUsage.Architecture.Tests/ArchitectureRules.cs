using System.Xml.Linq;

namespace TokenUsage.Architecture.Tests;

public static class ArchitectureRules
{
    public static IReadOnlyDictionary<string, IReadOnlySet<string>> AllowedReferences { get; } =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["TokenUsage.Core"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            ["TokenUsage.Providers"] = CreateSet("TokenUsage.Core"),
            ["TokenUsage.Platform.Windows"] = CreateSet("TokenUsage.Core"),
            ["TokenUsage.Runtime.Windows"] = CreateSet(
                "TokenUsage.Core",
                "TokenUsage.Providers",
                "TokenUsage.Platform.Windows"),
            ["TokenUsage.Presentation"] = CreateSet("TokenUsage.Core", "TokenUsage.Providers"),
            ["TokenUsage.App"] = CreateSet(
                "TokenUsage.Core",
                "TokenUsage.Providers",
                "TokenUsage.Platform.Windows",
                "TokenUsage.Runtime.Windows",
                "TokenUsage.Presentation"),
            ["TokenUsage.Cli"] = CreateSet(
                "TokenUsage.Core",
                "TokenUsage.Providers",
                "TokenUsage.Runtime.Windows"),
            ["TokenUsage.LocalApi"] = CreateSet(
                "TokenUsage.Core",
                "TokenUsage.Runtime.Windows"),
        };

    private static readonly IReadOnlySet<string> OptionalProjects = CreateSet(
        "TokenUsage.LocalApi");

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

    public static IReadOnlyList<string> FindPresentationIsolationViolations(string presentationProjectPath)
    {
        var violations = new List<string>();
        XDocument project = XDocument.Load(presentationProjectPath);

        string? targetFramework = project
            .Descendants("TargetFramework")
            .Select(element => element.Value.Trim())
            .FirstOrDefault();

        if (!string.Equals(targetFramework, "net10.0", StringComparison.OrdinalIgnoreCase))
        {
            violations.Add($"Presentation TargetFramework must be net10.0; found '{targetFramework}'.");
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
                violations.Add($"Presentation must not reference a UI or Windows package: {packageId}");
            }
        }

        return violations;
    }

    private static HashSet<string> CreateSet(params string[] values) =>
        new HashSet<string>(values, StringComparer.OrdinalIgnoreCase);
}
