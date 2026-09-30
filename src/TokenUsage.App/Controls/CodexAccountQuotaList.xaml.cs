using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TokenUsage.App.ViewModels;

namespace TokenUsage.App.Controls;

public sealed partial class CodexAccountQuotaList : UserControl
{
    public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(
        nameof(Items), typeof(IReadOnlyList<CodexAccountQuota>), typeof(CodexAccountQuotaList),
        new PropertyMetadata(null, (owner, args) =>
            ((CodexAccountQuotaList)owner).AccountItems.ItemsSource = args.NewValue));

    public CodexAccountQuotaList() => InitializeComponent();

    public IReadOnlyList<CodexAccountQuota>? Items
    {
        get => (IReadOnlyList<CodexAccountQuota>?)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }
}
