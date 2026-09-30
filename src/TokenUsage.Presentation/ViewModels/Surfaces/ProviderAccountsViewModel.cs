using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TokenUsage.Core.Providers;

namespace TokenUsage.App.ViewModels.Surfaces;

public sealed partial class ProviderAccountChoice : ObservableObject
{
    public ProviderAccountChoice(ProviderAccountInfo account, Func<string, string> text)
    {
        Key = account.InstanceKey.AccountKey!;
        Label = account.Alias ?? string.Format(CultureInfo.CurrentCulture, text("CodexAccountProfileFormat"), account.Number);
        if (account.IsActive) Label += " · " + text("CodexAccountActive");
        IsSelected = account.IsSelected;
        CanSelect = account.Status == ProviderAccountStatus.Available || account.IsSelected;
        Status = text(account.Status switch
        {
            ProviderAccountStatus.Available => "CodexAccountAvailable",
            ProviderAccountStatus.LoginRequired => "CodexAccountLoginRequired",
            ProviderAccountStatus.IdentityChanged => "CodexAccountIdentityChanged",
            _ => "CodexAccountUnavailable",
        });
    }
    public string Key { get; }
    public string Label { get; }
    public string Status { get; }
    public bool CanSelect { get; }
    public string AutomationId => "CodexAccountChoice." + Key;
    [ObservableProperty] public partial bool IsSelected { get; set; }
}

public sealed partial class ProviderAccountsViewModel(
    IProviderAccountService? service, Func<string, string> text, Func<Task>? refresh) : ObservableObject
{
    public bool IsAvailable => service is not null;
    [ObservableProperty] public partial IReadOnlyList<ProviderAccountChoice> Choices { get; private set; } = [];
    [ObservableProperty] public partial string StatusText { get; private set; } = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanSave))] public partial bool IsBusy { get; private set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanSave))] public partial bool HasLoaded { get; private set; }
    public bool CanSave => HasLoaded && !IsBusy;

    [RelayCommand]
    private async Task DiscoverAsync(CancellationToken token)
    {
        if (service is null || IsBusy) return;
        IsBusy = true;
        try
        {
            IReadOnlyList<ProviderAccountInfo> accounts = await service.DiscoverAsync(token);
            Choices = accounts.Select(account => new ProviderAccountChoice(account, text)).ToArray();
            StatusText = text(service.DiscoveryUnavailable ? "CodexAccountsDiscoveryFailed"
                : accounts.Count == 0 ? "CodexAccountsEmpty" : "CodexAccountsSelectionHint");
            HasLoaded = true;
        }
        catch (OperationCanceledException) { StatusText = text("CodexAccountsUpdateInterrupted"); }
        catch (Exception exception) when (IsExpectedFailure(exception)) { StatusText = text("CodexAccountsDiscoveryFailed"); }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken token)
    {
        if (service is null || !HasLoaded || IsBusy) return;
        IsBusy = true;
        try
        {
            await service.SaveSelectionAsync(Choices.Where(choice => choice.IsSelected).Select(choice => choice.Key).ToArray(), token);
            StatusText = text("CodexAccountsSaved");
            if (refresh is not null) await refresh();
        }
        catch (OperationCanceledException) { StatusText = text("CodexAccountsUpdateInterrupted"); }
        catch (Exception exception) when (IsExpectedFailure(exception)) { StatusText = text("CodexAccountsSaveFailed"); }
        finally { IsBusy = false; }
    }

    private static bool IsExpectedFailure(Exception exception) => exception is IOException
        or UnauthorizedAccessException or InvalidOperationException or ArgumentException or TimeoutException;
}
