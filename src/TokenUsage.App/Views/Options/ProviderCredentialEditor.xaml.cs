using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using TokenUsage.App.ViewModels.Surfaces;

namespace TokenUsage.App.Views.Options;

public sealed partial class ProviderCredentialEditor : UserControl
{
    public static readonly DependencyProperty ProviderIdProperty =
        DependencyProperty.Register(
            nameof(ProviderId),
            typeof(string),
            typeof(ProviderCredentialEditor),
            new PropertyMetadata(string.Empty, OnEditorStateChanged));

    public static readonly DependencyProperty ProviderNameProperty =
        DependencyProperty.Register(
            nameof(ProviderName),
            typeof(string),
            typeof(ProviderCredentialEditor),
            new PropertyMetadata(string.Empty, OnEditorStateChanged));

    public static readonly DependencyProperty HasSavedCredentialProperty =
        DependencyProperty.Register(
            nameof(HasSavedCredential),
            typeof(bool),
            typeof(ProviderCredentialEditor),
            new PropertyMetadata(false, OnEditorStateChanged));

    public static readonly DependencyProperty SecondaryFieldLabelProperty =
        DependencyProperty.Register(
            nameof(SecondaryFieldLabel),
            typeof(string),
            typeof(ProviderCredentialEditor),
            new PropertyMetadata(string.Empty, OnEditorStateChanged));

    public static readonly DependencyProperty SecondaryFieldPlaceholderProperty =
        DependencyProperty.Register(
            nameof(SecondaryFieldPlaceholder),
            typeof(string),
            typeof(ProviderCredentialEditor),
            new PropertyMetadata(string.Empty, OnEditorStateChanged));

    public static readonly DependencyProperty CredentialHelpTextProperty =
        DependencyProperty.Register(
            nameof(CredentialHelpText),
            typeof(string),
            typeof(ProviderCredentialEditor),
            new PropertyMetadata(string.Empty, OnEditorStateChanged));

    public static readonly DependencyProperty SecretFieldLabelProperty =
        DependencyProperty.Register(
            nameof(SecretFieldLabel),
            typeof(string),
            typeof(ProviderCredentialEditor),
            new PropertyMetadata(string.Empty, OnEditorStateChanged));

    public static readonly DependencyProperty SecretFieldPlaceholderProperty =
        DependencyProperty.Register(
            nameof(SecretFieldPlaceholder),
            typeof(string),
            typeof(ProviderCredentialEditor),
            new PropertyMetadata(string.Empty, OnEditorStateChanged));

    public ProviderCredentialEditor()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    public ProviderStatusSurfaceViewModel? Host { get; set; }

    public string ProviderId
    {
        get => (string)GetValue(ProviderIdProperty);
        set => SetValue(ProviderIdProperty, value);
    }

    public string ProviderName
    {
        get => (string)GetValue(ProviderNameProperty);
        set => SetValue(ProviderNameProperty, value);
    }

    public bool HasSavedCredential
    {
        get => (bool)GetValue(HasSavedCredentialProperty);
        set => SetValue(HasSavedCredentialProperty, value);
    }

    public string SecondaryFieldLabel
    {
        get => (string)GetValue(SecondaryFieldLabelProperty);
        set => SetValue(SecondaryFieldLabelProperty, value);
    }

    public string SecondaryFieldPlaceholder
    {
        get => (string)GetValue(SecondaryFieldPlaceholderProperty);
        set => SetValue(SecondaryFieldPlaceholderProperty, value);
    }

    public string CredentialHelpText
    {
        get => (string)GetValue(CredentialHelpTextProperty);
        set => SetValue(CredentialHelpTextProperty, value);
    }

    public string SecretFieldLabel
    {
        get => (string)GetValue(SecretFieldLabelProperty);
        set => SetValue(SecretFieldLabelProperty, value);
    }

    public string SecretFieldPlaceholder
    {
        get => (string)GetValue(SecretFieldPlaceholderProperty);
        set => SetValue(SecretFieldPlaceholderProperty, value);
    }

    private static void OnEditorStateChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is ProviderCredentialEditor editor)
        {
            editor.ApplyState();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => ApplyState();

    private void ApplyState()
    {
        if (ProviderTitle is null)
        {
            return;
        }

        ProviderTitle.Text = ProviderName;
        if (!string.IsNullOrEmpty(CredentialHelpText))
        {
            HelpText.Text = CredentialHelpText;
        }

        if (!string.IsNullOrEmpty(SecretFieldLabel))
        {
            ApiKeyBox.Header = SecretFieldLabel;
            ApiKeyBox.PlaceholderText = SecretFieldPlaceholder;
            AutomationProperties.SetName(ApiKeyBox, SecretFieldLabel);
        }

        bool hasSecondary = !string.IsNullOrEmpty(SecondaryFieldLabel);
        SecondaryBox.Header = SecondaryFieldLabel;
        SecondaryBox.PlaceholderText = SecondaryFieldPlaceholder;
        SecondaryBox.Visibility = hasSecondary ? Visibility.Visible : Visibility.Collapsed;
        RemoveButton.Visibility = HasSavedCredential ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetAutomationId(ApiKeyBox, $"ProviderCredential.{ProviderId}.ApiKey");
        AutomationProperties.SetAutomationId(SecondaryBox, $"ProviderCredential.{ProviderId}.Secondary");
        AutomationProperties.SetAutomationId(SaveButton, $"ProviderCredential.{ProviderId}.Save");
        AutomationProperties.SetAutomationId(RemoveButton, $"ProviderCredential.{ProviderId}.Remove");
    }

    private async void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        if (Host is null || string.IsNullOrWhiteSpace(ProviderId))
        {
            return;
        }

        SetBusy(true);
        ManualCredentialOperationResult result = await Host.SaveManualCredentialAsync(
            ProviderId,
            ApiKeyBox.Password,
            SecondaryBox.Text);
        SetBusy(false);
        if (result.Succeeded)
        {
            ApiKeyBox.Password = string.Empty;
            SecondaryBox.Text = string.Empty;
            CloseParentFlyout();
            return;
        }

        ShowStatus(result.StatusText);
    }

    private async void OnRemoveClicked(object sender, RoutedEventArgs e)
    {
        if (Host is null || string.IsNullOrWhiteSpace(ProviderId))
        {
            return;
        }

        SetBusy(true);
        ManualCredentialOperationResult result = await Host.DeleteManualCredentialAsync(ProviderId);
        SetBusy(false);
        if (result.Succeeded)
        {
            ApiKeyBox.Password = string.Empty;
            SecondaryBox.Text = string.Empty;
            CloseParentFlyout();
            return;
        }

        ShowStatus(result.StatusText);
    }

    private void SetBusy(bool isBusy)
    {
        ApiKeyBox.IsEnabled = !isBusy;
        SecondaryBox.IsEnabled = !isBusy;
        SaveButton.IsEnabled = !isBusy;
        RemoveButton.IsEnabled = !isBusy;
        if (isBusy)
        {
            ShowStatus(Host?.CredentialBusyText ?? string.Empty);
        }
    }

    private void ShowStatus(string text)
    {
        StatusText.Text = text;
        StatusText.Visibility = string.IsNullOrWhiteSpace(text)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void CloseParentFlyout()
    {
        DependencyObject current = this;
        while (current is not null)
        {
            if (current is FlyoutPresenter && VisualTreeHelper.GetParent(current) is Popup popup)
            {
                popup.IsOpen = false;
                return;
            }

            current = VisualTreeHelper.GetParent(current);
        }
    }
}
