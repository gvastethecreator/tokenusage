using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WOpenUsage.App.Localization;
using WOpenUsage.App.ViewModels.Surfaces;

namespace WOpenUsage.App.Views.Options;

public sealed partial class GeneralOptionsView : UserControl
{
    private GeneralOptionsViewModel? _viewModel;
    private bool _isInitialized;

    public GeneralOptionsView()
    {
        InitializeComponent();
        _isInitialized = true;
    }

    public GeneralOptionsViewModel? ViewModel
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

    public UIElement PrimaryAction => CloseWhenInactiveToggle;

    private void OnRestartForLanguageClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        try
        {
            AppLanguageRuntime.RestartWithLanguage(
                ViewModel.PendingLanguageTag,
                GetLanguageRestartArguments());
        }
        catch (Exception exception) when (exception is ArgumentException
            or InvalidOperationException
            or UnauthorizedAccessException
            or System.Runtime.InteropServices.COMException)
        {
        }

        ViewModel.ReportLanguageRestartFailure();
    }

    private static string GetLanguageRestartArguments()
    {
#if DEBUG || UI_TEST_FIXTURES
        return AppLanguageRestartArguments.Create(Environment.GetCommandLineArgs()[1..]);
#else
        return string.Empty;
#endif
    }
}
