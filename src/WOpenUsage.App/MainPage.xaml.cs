using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using WOpenUsage.App.ViewModels;

namespace WOpenUsage.App;

public sealed partial class MainPage : Page
{
    public MainPage()
    {
        InitializeComponent();
        KeyDown += OnKeyDown;
    }

    public event EventHandler? HideRequested;

    public FlyoutViewModel ViewModel { get; } = new();

    public FrameworkElement MeasureRoot => FlyoutChrome;

    public void FocusPrimaryAction()
    {
        UIElement target = ViewModel.SurfaceState switch
        {
            FlyoutSurfaceState.Options => CloseWhenInactiveToggle,
            FlyoutSurfaceState.Loading => FooterOptionsButton,
            _ => EmptyOpenOptionsButton,
        };

        _ = target.Focus(FocusState.Programmatic);
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape)
        {
            return;
        }

        if (ViewModel.IsOptions)
        {
            ViewModel.CloseOptionsCommand.Execute(null);
        }
        else
        {
            HideRequested?.Invoke(this, EventArgs.Empty);
        }

        e.Handled = true;
    }
}
