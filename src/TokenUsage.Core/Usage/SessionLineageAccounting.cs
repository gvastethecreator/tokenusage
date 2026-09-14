namespace TokenUsage.Core.Usage;

public static class SessionLineageAccounting
{
    public static IReadOnlyList<UsageSessionLineageNode> Build(
        IReadOnlyList<UsageSessionContribution> sessions,
        ParentChildAccountingKind accounting)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        if (!Enum.IsDefined(accounting))
        {
            throw new ArgumentOutOfRangeException(nameof(accounting));
        }

        var nodes = new Dictionary<string, UsageSessionContribution>(StringComparer.Ordinal);
        foreach (UsageSessionContribution session in sessions)
        {
            if (session.IsUnassigned || session.SessionKey is null)
            {
                continue;
            }

            nodes[session.SessionKey.Value] = session;
        }

        var children = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var parentOf = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (UsageSessionContribution session in nodes.Values)
        {
            if (session.ParentSessionKey is null)
            {
                continue;
            }

            string child = session.SessionKey!.Value;
            string parent = session.ParentSessionKey.Value;
            if (WouldCreateCycle(parentOf, child, parent))
            {
                continue;
            }

            parentOf[child] = parent;
            if (!children.TryGetValue(parent, out List<string>? list))
            {
                list = [];
                children[parent] = list;
            }

            if (!list.Contains(child, StringComparer.Ordinal))
            {
                list.Add(child);
            }
        }

        var result = new List<UsageSessionLineageNode>(nodes.Count);
        foreach (UsageSessionContribution session in nodes.Values.OrderBy(row => row.SessionKey!.Value, StringComparer.Ordinal))
        {
            string key = session.SessionKey!.Value;
            IReadOnlyList<OpaqueAttributionKey> childKeys = children.TryGetValue(key, out List<string>? ids)
                ? ids.OrderBy(id => id, StringComparer.Ordinal)
                    .Select(id => new OpaqueAttributionKey(id))
                    .ToArray()
                : [];
            TokenBreakdown exclusive = session.SessionTokens;
            TokenBreakdown? inclusive = accounting switch
            {
                ParentChildAccountingKind.Unknown => null,
                ParentChildAccountingKind.Inclusive => session.SessionTokens,
                _ => AddDistinctDescendants(key, exclusive, nodes, children, new HashSet<string>(StringComparer.Ordinal)),
            };
            OpaqueAttributionKey? parentKey = parentOf.TryGetValue(key, out string? parentId)
                ? new OpaqueAttributionKey(parentId)
                : null;
            result.Add(new UsageSessionLineageNode(
                session.SessionKey,
                parentKey,
                exclusive,
                inclusive,
                session.SessionEventCount,
                childKeys));
        }

        return result;
    }

    private static bool WouldCreateCycle(
        Dictionary<string, string> parentOf,
        string child,
        string parent)
    {
        if (string.Equals(child, parent, StringComparison.Ordinal))
        {
            return true;
        }

        string? cursor = parent;
        var seen = new HashSet<string>(StringComparer.Ordinal) { child };
        while (cursor is not null)
        {
            if (!seen.Add(cursor))
            {
                return true;
            }

            if (!parentOf.TryGetValue(cursor, out cursor))
            {
                break;
            }
        }

        return false;
    }

    private static TokenBreakdown AddDistinctDescendants(
        string root,
        TokenBreakdown exclusive,
        Dictionary<string, UsageSessionContribution> nodes,
        Dictionary<string, List<string>> children,
        HashSet<string> visiting)
    {
        if (!visiting.Add(root) || !children.TryGetValue(root, out List<string>? ids))
        {
            return exclusive;
        }

        TokenBreakdown total = exclusive;
        foreach (string child in ids)
        {
            if (!nodes.TryGetValue(child, out UsageSessionContribution? session)
                || visiting.Contains(child))
            {
                continue;
            }

            total = AddTokens(total, session.SessionTokens);
            total = AddDistinctDescendants(child, total, nodes, children, visiting);
        }

        return total;
    }

    private static TokenBreakdown AddTokens(TokenBreakdown left, TokenBreakdown right) =>
        new(
            checked(left.Input + right.Input),
            checked(left.Output + right.Output),
            checked(left.Reasoning + right.Reasoning),
            checked(left.CacheRead + right.CacheRead),
            checked(left.CacheWrite + right.CacheWrite));
}
