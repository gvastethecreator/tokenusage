using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TokenUsage.App.ViewModels;

namespace TokenUsage.App.Controls;

public sealed partial class AccountQuotaList : UserControl
{
    public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(
        nameof(Items), typeof(IReadOnlyList<AccountQuota>), typeof(AccountQuotaList),
        new PropertyMetadata(null, (owner, args) =>
            ((AccountQuotaList)owner).AccountItems.ItemsSource = args.NewValue));

    public AccountQuotaList() => InitializeComponent();

    public IReadOnlyList<AccountQuota>? Items
    {
        get => (IReadOnlyList<AccountQuota>?)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }
}
