namespace TokenUsage.App.ViewModels;

public sealed record AppearanceOption<TValue>(TValue Value, string DisplayName)
    where TValue : struct, Enum;
