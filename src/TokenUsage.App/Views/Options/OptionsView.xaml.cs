using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TokenUsage.App.ViewModels;
using TokenUsage.App.ViewModels.Surfaces;
using TokenUsage.Core.Appearance;
using Microsoft.Windows.ApplicationModel.Resources;
using Windows.ApplicationModel;
using System.Reflection;
using Microsoft.UI.Xaml.Media.Animation;
using TokenUsage.App.Controls;
using System.Runtime.InteropServices;

namespace TokenUsage.App.Views.Options;

public sealed partial class OptionsView : UserControl
{
    private readonly ResourceLoader _resources = new();
    private OptionsSurfaceViewModel? _viewModel;
    private bool _isInitialized;
    private int _selectedTab;
    private Storyboard? _panelTransition;
    private double _targetHeight;
    private bool _measurePending;
    public event EventHandler? LayoutAnimationProgressed;

    public OptionsView()
    {
        InitializeComponent();
        _isInitialized = true;
        Loaded += (_, _) => UpdatePanelHeight(animate: false);
        Unloaded += (_, _) => StopPanelTransition();
        SizeChanged += (_, e) => { if (e.PreviousSize.Width != e.NewSize.Width) QueuePanelMeasure(); };
        foreach (ScrollViewer scroll in Panels)
        {
            ((FrameworkElement)scroll.Content).SizeChanged += (_, _) => QueuePanelMeasure();
        }
    }

    public OptionsSurfaceViewModel? ViewModel
    {
        get => _viewModel;
        set
        {
            _viewModel = value ?? throw new ArgumentNullException(nameof(value));
            if (_isInitialized)
            {
                Bindings.Update();
            }
        }
    }

    public UIElement GetPrimaryAction(OptionsSection section)
    {
        int index = section switch
        {
            OptionsSection.General => 0,
            OptionsSection.Notifications => 1,
            OptionsSection.Appearance or OptionsSection.Personalization => 2,
            OptionsSection.Providers or OptionsSection.ProviderStatus => 3,
            _ => _selectedTab,
        };
        SelectPanel(index);

        if (section == OptionsSection.Personalization)
        {
            return AppearanceView.PersonalizationPrimaryAction;
        }

        return _selectedTab switch
        {
            1 => NotificationsView.PrimaryAction,
            2 => AppearanceView.PrimaryAction,
            3 => ProviderStatusView.PrimaryAction,
            _ => GeneralView.PrimaryAction,
        };
    }

    private ScrollViewer[] Panels => [GeneralScroll, NotificationsScroll, AppearanceScroll, ProvidersScroll];

    private void OnOptionTabChecked(object sender, RoutedEventArgs e)
    {
        if (_isInitialized && sender is RadioButton { Tag: string tag } && int.TryParse(tag, out int index))
            SelectPanel(index);
    }

    private void SelectPanel(int index)
    {
        if (index == _selectedTab) return;
        _selectedTab = index;
        RadioButton[] tabs = [GeneralTab, NotificationsTab, AppearanceTab, ProvidersTab];
        for (int slot = 0; slot < tabs.Length; slot++)
        {
            tabs[slot].IsChecked = slot == index;
            Panels[slot].Visibility = slot == index ? Visibility.Visible : Visibility.Collapsed;
        }
        UpdatePanelHeight(animate: true);
    }

    private void QueuePanelMeasure()
    {
        if (_measurePending || !IsLoaded) return;
        _measurePending = true;
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            _measurePending = false;
            if (IsLoaded) UpdatePanelHeight(animate: true);
        });
    }

    private void StopPanelTransition()
    {
        double height = OptionsContentHost.ActualHeight;
        _panelTransition?.Stop();
        _panelTransition = null;
        OptionsContentHost.Height = height;
        foreach (ScrollViewer panel in Panels) panel.Opacity = 1;
    }

    private void UpdatePanelHeight(bool animate)
    {
        if (ActualWidth <= 0) return;
        ScrollViewer panel = Panels[_selectedTab];
        var content = (FrameworkElement)panel.Content;
        content.Measure(new Windows.Foundation.Size(ActualWidth, double.PositiveInfinity));
        double height = Math.Clamp(content.DesiredSize.Height, 120, 540);
        if (Math.Abs(_targetHeight - height) < 0.5 && _panelTransition is not null) return;
        double start = OptionsContentHost.ActualHeight;
        StopPanelTransition();
        _targetHeight = height;
        if (!animate || !IsLoaded || !MotionSettings.AreAnimationsEnabled() || Math.Abs(start - height) < 0.5)
        {
            OptionsContentHost.Height = height;
            return;
        }

        var storyboard = new Storyboard();
        var resize = new DoubleAnimation
        {
            From = start, To = height, Duration = MotionSettings.VisualizationSwitchDuration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            EnableDependentAnimation = true,
        };
        Storyboard.SetTarget(resize, OptionsContentHost);
        Storyboard.SetTargetProperty(resize, nameof(Height));
        storyboard.Children.Add(resize);
        var fade = new DoubleAnimation { From = 0.65, To = 1, Duration = MotionSettings.VisualizationSwitchDuration };
        Storyboard.SetTarget(fade, panel);
        Storyboard.SetTargetProperty(fade, nameof(Opacity));
        storyboard.Children.Add(fade);
        storyboard.Completed += (_, _) =>
        {
            if (!ReferenceEquals(_panelTransition, storyboard)) return;
            StopPanelTransition();
            OptionsContentHost.Height = height;
        };
        _panelTransition = storyboard;
        storyboard.Begin();
    }

    private void OnOptionsHostSizeChanged(object sender, SizeChangedEventArgs e)
    {
        OptionsContentClip.Rect = new Windows.Foundation.Rect(0, 0, e.NewSize.Width, e.NewSize.Height);
        LayoutAnimationProgressed?.Invoke(this, EventArgs.Empty);
    }

    public string VersionText
    {
        get
        {
            string version = GetProductVersion();
            return string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                GetString("AboutVersionFormat"),
                version);
        }
    }

    public void ApplyAppearance(AppearanceSettings settings, double width)
    {
        ArgumentNullException.ThrowIfNull(settings);
        OptionsStack.Spacing = settings.Density == AppDensityMode.Compact ? 8 : 12;
        AppearanceView.ApplyLayout(width);
    }

    private string GetString(string key)
    {
        string value = _resources.GetString(key);
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"The resource '{key}' is missing.")
            : value;
    }

    private static string GetProductVersion()
    {
        try
        {
            PackageVersion version = Package.Current.Id.Version;
            return $"{version.Major}.{version.Minor}.{version.Build}";
        }
        catch (Exception exception) when (exception is InvalidOperationException or COMException)
        {
            string? informationalVersion = typeof(OptionsView).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;
            return informationalVersion?.Split('+', 2)[0]
                ?? typeof(OptionsView).Assembly.GetName().Version?.ToString(3)
                ?? "0.0.1";
        }
    }
}
