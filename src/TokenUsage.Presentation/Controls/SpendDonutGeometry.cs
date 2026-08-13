namespace TokenUsage.App.Controls;

public readonly record struct SpendDonutInput(string ProviderId, double Amount);

public readonly record struct SpendDonutArc(
    string ProviderId,
    double TrueShare,
    double StartFraction,
    double EndFraction);

public static class SpendDonutGeometry
{
    public const double MinimumDisplayShare = 0.008;

    public static IReadOnlyList<SpendDonutArc> CreateArcs(
        IEnumerable<SpendDonutInput> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        SpendDonutInput[] inputs = values.ToArray();
        foreach (SpendDonutInput input in inputs)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(input.ProviderId);
        }

        var sanitized = inputs
            .Select(input => new SanitizedInput(
                input.ProviderId,
                double.IsFinite(input.Amount) && input.Amount > 0 ? input.Amount : 0))
            .Where(input => input.Amount > 0)
            .ToArray();

        double total = sanitized.Sum(input => input.Amount);
        if (!double.IsFinite(total) || total <= 0)
        {
            return [];
        }

        var shares = sanitized
            .Select(input =>
            {
                double trueShare = input.Amount / total;
                return new DisplayShare(
                    input.ProviderId,
                    trueShare,
                    Math.Max(trueShare, MinimumDisplayShare));
            })
            .ToArray();

        double displayTotal = shares.Sum(share => share.DisplayValue);
        var result = new SpendDonutArc[shares.Length];
        double cursor = 0;

        for (int index = 0; index < shares.Length; index++)
        {
            DisplayShare share = shares[index];
            double end = index == shares.Length - 1
                ? 1
                : cursor + (share.DisplayValue / displayTotal);

            result[index] = new SpendDonutArc(
                share.ProviderId,
                share.TrueShare,
                cursor,
                end);
            cursor = end;
        }

        return result;
    }

    public static double ClampPercent(double value) =>
        double.IsFinite(value) ? Math.Clamp(value, 0, 100) : 0;

    private readonly record struct SanitizedInput(string ProviderId, double Amount);

    private readonly record struct DisplayShare(
        string ProviderId,
        double TrueShare,
        double DisplayValue);
}
