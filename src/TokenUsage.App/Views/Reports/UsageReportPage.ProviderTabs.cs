using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using TokenUsage.App.Controls;
using TokenUsage.App.ViewModels;
using TokenUsage.App.ViewModels.Reports;

namespace TokenUsage.App.Views.Reports;

public sealed partial class UsageReportPage
{
    private void OnProviderTabClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string providerId })
        {
            SelectProvider(providerId);
        }
    }

    private void OnPreviousProviderTabClick(object sender, RoutedEventArgs e) =>
        NavigateProviderTab(-1);

    private void OnNextProviderTabClick(object sender, RoutedEventArgs e) =>
        NavigateProviderTab(1);

    private void SelectProvider(string providerId)
    {
        UsageReportProviderOption? option = ViewModel.ProviderOptions
            .FirstOrDefault(candidate => string.Equals(
                candidate.ProviderId,
                providerId,
                StringComparison.Ordinal));
        if (option is null)
        {
            return;
        }

        int previousIndex = IndexOfProvider(ViewModel.SelectedProvider?.ProviderId);
        int nextIndex = IndexOfProvider(providerId);
        int direction = nextIndex < previousIndex ? -1 : 1;
        bool changesProvider = previousIndex != nextIndex;
        EnsureProviderTabVisible(nextIndex, direction, animate: changesProvider);
        if (!ShouldStartReportDataTransition(
            ReportDataTransitionIntent.Provider,
            changesProvider))
        {
            SetProviderTabSelection(providerId);
            return;
        }

        PlayReportDataTransition(
            () => ViewModel.SelectedProvider = option,
            ReportDataTransitionIntent.Provider);
    }

    private void NavigateProviderTab(int direction)
    {
        int selectedIndex = IndexOfProvider(ViewModel.SelectedProvider?.ProviderId);
        if (selectedIndex < 0)
        {
            if (ViewModel.ProviderOptions.Count > 0)
            {
                SelectProvider(ViewModel.ProviderOptions[0].ProviderId);
            }

            return;
        }

        int targetIndex = Math.Clamp(
            selectedIndex + direction,
            0,
            Math.Max(0, ViewModel.ProviderOptions.Count - 1));
        if (targetIndex == selectedIndex || targetIndex >= ViewModel.ProviderOptions.Count)
        {
            return;
        }

        SelectProvider(ViewModel.ProviderOptions[targetIndex].ProviderId);
    }

    private int IndexOfProvider(string? providerId)
    {
        if (providerId is null)
        {
            return -1;
        }

        for (int index = 0; index < ViewModel.ProviderOptions.Count; index++)
        {
            if (string.Equals(
                ViewModel.ProviderOptions[index].ProviderId,
                providerId,
                StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private int IndexOfVisibleProvider(string? providerId)
    {
        if (providerId is null)
        {
            return -1;
        }

        for (int index = 0; index < _visibleProviderTabs.Count; index++)
        {
            if (string.Equals(
                _visibleProviderTabs[index].ProviderId,
                providerId,
                StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private void SynchronizeProviderTabs()
    {
        _ = UpdateProviderTabPageSize();
        int selectedIndex = IndexOfProvider(ViewModel.SelectedProvider?.ProviderId);
        int maxStart = Math.Max(0, ViewModel.ProviderOptions.Count - _providerTabPageSize);
        _providerTabStartIndex = Math.Clamp(_providerTabStartIndex, 0, maxStart);
        if (selectedIndex >= 0)
        {
            if (selectedIndex < _providerTabStartIndex)
            {
                _providerTabStartIndex = selectedIndex;
            }
            else if (selectedIndex >= _providerTabStartIndex + _providerTabPageSize)
            {
                _providerTabStartIndex = selectedIndex - _providerTabPageSize + 1;
            }
        }

        ReplaceVisibleProviderTabs();
        UpdateProviderTabNavigationButtons();
        SetProviderTabSelection(ViewModel.SelectedProvider?.ProviderId);
    }

    private void EnsureProviderTabVisible(int providerIndex, int direction, bool animate)
    {
        if (providerIndex < 0 || providerIndex >= ViewModel.ProviderOptions.Count)
        {
            UpdateProviderTabNavigationButtons();
            return;
        }

        int nextStart = _providerTabStartIndex;
        if (providerIndex < nextStart)
        {
            nextStart = providerIndex;
        }
        else if (providerIndex >= nextStart + _providerTabPageSize)
        {
            nextStart = providerIndex - _providerTabPageSize + 1;
        }

        int maxStart = Math.Max(0, ViewModel.ProviderOptions.Count - _providerTabPageSize);
        nextStart = Math.Clamp(nextStart, 0, maxStart);
        if (nextStart == _providerTabStartIndex)
        {
            UpdateProviderTabNavigationButtons(providerIndex);
            SetProviderTabSelection(ViewModel.ProviderOptions[providerIndex].ProviderId);
            return;
        }

        _providerTabStartIndex = nextStart;
        ReplaceVisibleProviderTabs();
        UpdateProviderTabNavigationButtons(providerIndex);
        SetProviderTabSelection(ViewModel.ProviderOptions[providerIndex].ProviderId);
        if (animate && IsLoaded && MotionSettings.AreAnimationsEnabled())
        {
            PlayProviderTabsTransition(direction);
        }
        else
        {
            CancelProviderTabsTransition();
        }
    }

    private void ReplaceVisibleProviderTabs()
    {
        int end = Math.Min(
            ViewModel.ProviderOptions.Count,
            _providerTabStartIndex + _providerTabPageSize);
        int count = end - _providerTabStartIndex;
        bool alreadySynchronized = _visibleProviderTabs.Count == count;
        for (int visibleIndex = 0; alreadySynchronized && visibleIndex < count; visibleIndex++)
        {
            alreadySynchronized = ReferenceEquals(
                _visibleProviderTabs[visibleIndex],
                ViewModel.ProviderOptions[_providerTabStartIndex + visibleIndex]);
        }

        if (alreadySynchronized)
        {
            UpdateProviderTabLayout();
            return;
        }

        _visibleProviderTabs.Clear();
        for (int index = _providerTabStartIndex; index < end; index++)
        {
            _visibleProviderTabs.Add(ViewModel.ProviderOptions[index]);
        }

        _ = DispatcherQueue.TryEnqueue(UpdateProviderTabLayout);
    }

    private void UpdateProviderTabNavigationButtons(int? selectedIndexOverride = null)
    {
        int providerCount = ViewModel.ProviderOptions.Count;
        bool hasOverflow = providerCount > _providerTabPageSize;
        ReportPreviousProviderButton.Visibility = hasOverflow
            ? Visibility.Visible
            : Visibility.Collapsed;
        ReportNextProviderButton.Visibility = hasOverflow
            ? Visibility.Visible
            : Visibility.Collapsed;
        ReportProviderCarousel.ColumnSpacing = hasOverflow ? 4 : 0;

        int selectedIndex = selectedIndexOverride
            ?? IndexOfProvider(ViewModel.SelectedProvider?.ProviderId);
        ReportPreviousProviderButton.IsEnabled = hasOverflow && selectedIndex > 0;
        ReportNextProviderButton.IsEnabled = hasOverflow
            && selectedIndex >= 0
            && selectedIndex < providerCount - 1;
    }

    private void OnProviderTabsViewportSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ReportProviderTabsClip.Rect = new Windows.Foundation.Rect(
            0,
            0,
            Math.Max(0, e.NewSize.Width),
            Math.Max(0, e.NewSize.Height));
        if (UpdateProviderTabPageSize())
        {
            SynchronizeProviderTabs();
            return;
        }

        UpdateProviderTabLayout();
    }

    private bool UpdateProviderTabPageSize()
    {
        int providerCount = ViewModel.ProviderOptions.Count;
        if (providerCount == 0)
        {
            return false;
        }

        int nextPageSize = ProviderTabCarouselLayout.PageSize(
            providerCount,
            ReportProviderCarousel.ActualWidth,
            ProviderTabCarouselLayout.ReportMaximumPageSize);
        if (nextPageSize == _providerTabPageSize)
        {
            return false;
        }

        _providerTabPageSize = nextPageSize;
        return true;
    }

    private void UpdateProviderTabLayout()
    {
        if (_visibleProviderTabs.Count == 0 || ReportProviderTabsViewport.ActualWidth <= 0)
        {
            return;
        }

        double spacing = ProviderTabCarouselLayout.Spacing;
        int count = _visibleProviderTabs.Count;
        _providerTabItemWidth = ProviderTabCarouselLayout.ItemWidth(
            ReportProviderTabsViewport.ActualWidth,
            count);
        ReportProviderTabsLayout.Spacing = spacing;
        for (int index = 0; index < count; index++)
        {
            if (ReportProviderTabsRepeater.TryGetElement(index) is RadioButton tab)
            {
                ApplyProviderTabSize(tab);
            }
        }
    }

    private void ApplyProviderTabSize(RadioButton tab)
    {
        tab.MinWidth = 0;
        tab.MaxWidth = _providerTabItemWidth;
        tab.Width = _providerTabItemWidth;
        tab.Margin = new Thickness(0);
        tab.HorizontalAlignment = HorizontalAlignment.Stretch;
        tab.HorizontalContentAlignment = HorizontalAlignment.Center;
        tab.VerticalAlignment = VerticalAlignment.Stretch;
        tab.VerticalContentAlignment = VerticalAlignment.Center;
    }

    private void SetProviderTabSelection(string? providerId)
    {
        int providerIndex = IndexOfVisibleProvider(providerId);
        if (providerIndex >= 0
            && ReportProviderTabsRepeater.TryGetElement(providerIndex) is RadioButton tab
            && tab.IsChecked != true)
        {
            tab.IsChecked = true;
        }
    }

    private void OnProviderTabPrepared(
        ItemsRepeater sender,
        ItemsRepeaterElementPreparedEventArgs args)
    {
        if (args.Element is RadioButton tab
            && args.Index >= 0
            && args.Index < _visibleProviderTabs.Count)
        {
            tab.IsChecked = string.Equals(
                _visibleProviderTabs[args.Index].ProviderId,
                ViewModel.SelectedProvider?.ProviderId,
                StringComparison.Ordinal);
            ApplyProviderTabSize(tab);
        }
    }

    private void PlayProviderTabsTransition(int direction)
    {
        int transitionToken = ++_providerTabsTransitionToken;
        _providerTabsStoryboard?.Stop();
        _providerTabsStoryboard = null;
        ReportProviderTabsTransitionRoot.Opacity = MotionSettings.ProviderCarouselMinimumOpacity;
        ReportProviderTabsTransitionTransform.TranslateX =
            MotionSettings.ProviderCarouselOffset * direction;

        var opacity = new DoubleAnimation
        {
            From = ReportProviderTabsTransitionRoot.Opacity,
            To = 1,
            Duration = MotionSettings.ProviderCarouselDuration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        var translation = new DoubleAnimation
        {
            From = ReportProviderTabsTransitionTransform.TranslateX,
            To = 0,
            Duration = MotionSettings.ProviderCarouselDuration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(opacity, ReportProviderTabsTransitionRoot);
        Storyboard.SetTargetProperty(opacity, nameof(Opacity));
        Storyboard.SetTarget(translation, ReportProviderTabsTransitionTransform);
        Storyboard.SetTargetProperty(translation, nameof(CompositeTransform.TranslateX));

        var storyboard = new Storyboard();
        storyboard.Children.Add(opacity);
        storyboard.Children.Add(translation);
        storyboard.Completed += (_, _) =>
        {
            if (transitionToken != _providerTabsTransitionToken
                || !ReferenceEquals(_providerTabsStoryboard, storyboard))
            {
                return;
            }

            CancelProviderTabsTransition();
        };
        _providerTabsStoryboard = storyboard;
        storyboard.Begin();
    }

    private void CancelProviderTabsTransition()
    {
        _providerTabsTransitionToken++;
        _providerTabsStoryboard?.Stop();
        _providerTabsStoryboard = null;
        ReportProviderTabsTransitionRoot.Opacity = 1;
        ReportProviderTabsTransitionTransform.TranslateX = 0;
    }
}
