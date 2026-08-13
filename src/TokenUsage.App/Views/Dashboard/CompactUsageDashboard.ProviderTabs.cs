using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using TokenUsage.App.Controls;
using TokenUsage.App.ViewModels;
using TokenUsage.App.ViewModels.Dashboard;
using TokenUsage.App.ViewModels.Surfaces;
using TokenUsage.Core.Appearance;

namespace TokenUsage.App.Views.Dashboard;

public sealed partial class CompactUsageDashboard
{
    private void OnProviderClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string providerId })
        {
            SetProviderTabSelection(providerId);
            SelectProviderWithTransition(providerId);
        }
    }

    private void OnPreviousProviderTabClick(object sender, RoutedEventArgs e) =>
        NavigateProviderTab(-1);

    private void OnNextProviderTabClick(object sender, RoutedEventArgs e) =>
        NavigateProviderTab(1);

    private void OnProviderSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel.IsProviderScope
            && sender is ComboBox { SelectedItem: DashboardProviderOption option })
        {
            SelectProviderWithTransition(option.ProviderId);
        }
    }

    private void OnDonutProviderInvoked(object? sender, ProviderInvokedEventArgs e) =>
        SelectProviderWithTransition(e.ProviderId);

    private void SelectProviderWithTransition(string providerId)
    {
        int previousIndex = IndexOfProvider(ViewModel.SelectedProvider?.ProviderId);
        int nextIndex = IndexOfProvider(providerId);
        int direction = nextIndex < previousIndex ? -1 : 1;
        EnsureProviderTabVisible(nextIndex, direction, animate: true);
        if (previousIndex == nextIndex && ViewModel.IsProviderScope)
        {
            SetProviderTabSelection(ViewModel.SelectedProvider?.ProviderId);
            return;
        }

        bool forceLimitsReveal = !ViewModel.IsProviderScope;
        if (!MotionSettings.AreAnimationsEnabled())
        {
            CancelProviderTransition();
            CommitProviderSelection(providerId, animateLimits: false, forceLimitsReveal);
            return;
        }

        Action commit = () => CommitProviderSelection(
            providerId,
            animateLimits: true,
            forceLimitsReveal);
        if (ViewModel.IsProviderScope)
        {
            PlayProviderContentTransition(commit);
            return;
        }

        PlayProviderTransition(
            ScopeTransitionRoot,
            ScopeTransitionTransform,
            commit,
            direction);
    }

    private void CommitProviderSelection(
        string providerId,
        bool animateLimits,
        bool forceLimitsReveal)
    {
        _suppressProviderLimitsPropertyTransition = true;
        try
        {
            ViewModel.SelectProvider(providerId);
        }
        finally
        {
            _suppressProviderLimitsPropertyTransition = false;
        }

        SetProviderTabSelection(ViewModel.SelectedProvider?.ProviderId);
        if (!animateLimits)
        {
            SynchronizeProviderLimitsImmediately();
            return;
        }

        bool shouldShow = ViewModel.SelectedProviderHasLimits;
        _ = DispatcherQueue.TryEnqueue(() =>
            PlayProviderLimitsTransition(shouldShow, forceLimitsReveal));
    }

    private void ShowGlobalWithTransition()
    {
        if (!ViewModel.IsProviderScope)
        {
            return;
        }

        StopProviderLimitsTransition();
        if (!MotionSettings.AreAnimationsEnabled())
        {
            CancelProviderTransition();
            ViewModel.ShowGlobal();
            return;
        }

        PlayProviderTransition(
            ScopeTransitionRoot,
            ScopeTransitionTransform,
            ViewModel.ShowGlobal,
            direction: -1);
    }

    private int IndexOfProvider(string? providerId)
    {
        if (providerId is null)
        {
            return -1;
        }

        for (int index = 0; index < ViewModel.ProviderSummaries.Count; index++)
        {
            if (string.Equals(
                ViewModel.ProviderSummaries[index].ProviderId,
                providerId,
                StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private void SetProviderTabSelection(string? providerId)
    {
        int providerIndex = IndexOfVisibleProvider(providerId);
        if (providerIndex >= 0
            && ProviderTabsRepeater.TryGetElement(providerIndex) is RadioButton tab
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

    private void NavigateProviderTab(int direction)
    {
        int selectedIndex = IndexOfProvider(ViewModel.SelectedProvider?.ProviderId);
        if (selectedIndex < 0)
        {
            if (ViewModel.ProviderSummaries.Count > 0)
            {
                SelectProviderWithTransition(ViewModel.ProviderSummaries[0].ProviderId);
            }

            return;
        }

        int targetIndex = Math.Clamp(
            selectedIndex + direction,
            0,
            Math.Max(0, ViewModel.ProviderSummaries.Count - 1));
        if (targetIndex == selectedIndex || targetIndex >= ViewModel.ProviderSummaries.Count)
        {
            return;
        }

        SelectProviderWithTransition(ViewModel.ProviderSummaries[targetIndex].ProviderId);
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
        if (_viewModel is null)
        {
            return;
        }

        _ = UpdateProviderTabPageSize();
        int selectedIndex = IndexOfProvider(ViewModel.SelectedProvider?.ProviderId);
        int maxStart = Math.Max(0, ViewModel.ProviderSummaries.Count - _providerTabPageSize);
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
        if (providerIndex < 0 || providerIndex >= ViewModel.ProviderSummaries.Count)
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

        int maxStart = Math.Max(0, ViewModel.ProviderSummaries.Count - _providerTabPageSize);
        nextStart = Math.Clamp(nextStart, 0, maxStart);
        if (nextStart == _providerTabStartIndex)
        {
            UpdateProviderTabNavigationButtons(providerIndex);
            return;
        }

        _providerTabStartIndex = nextStart;
        ReplaceVisibleProviderTabs();
        UpdateProviderTabNavigationButtons(providerIndex);
        SetProviderTabSelection(ViewModel.ProviderSummaries[providerIndex].ProviderId);
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
            ViewModel.ProviderSummaries.Count,
            _providerTabStartIndex + _providerTabPageSize);
        int count = end - _providerTabStartIndex;
        bool alreadySynchronized = _visibleProviderTabs.Count == count;
        for (int visibleIndex = 0; alreadySynchronized && visibleIndex < count; visibleIndex++)
        {
            alreadySynchronized = ReferenceEquals(
                _visibleProviderTabs[visibleIndex],
                ViewModel.ProviderSummaries[_providerTabStartIndex + visibleIndex]);
        }

        if (alreadySynchronized)
        {
            UpdateProviderTabLayout();
            return;
        }

        _visibleProviderTabs.Clear();
        for (int index = _providerTabStartIndex; index < end; index++)
        {
            _visibleProviderTabs.Add(ViewModel.ProviderSummaries[index]);
        }

        _ = DispatcherQueue.TryEnqueue(UpdateProviderTabLayout);

    }

    private void UpdateProviderTabNavigationButtons(int? selectedIndexOverride = null)
    {
        int providerCount = ViewModel.ProviderSummaries.Count;
        bool hasOverflow = providerCount > _providerTabPageSize;
        PreviousProviderTabButton.Visibility = hasOverflow
            ? Visibility.Visible
            : Visibility.Collapsed;
        NextProviderTabButton.Visibility = hasOverflow
            ? Visibility.Visible
            : Visibility.Collapsed;
        ProviderTabCarousel.ColumnSpacing = hasOverflow ? 4 : 0;

        int selectedIndex = selectedIndexOverride
            ?? IndexOfProvider(ViewModel.SelectedProvider?.ProviderId);
        PreviousProviderTabButton.IsEnabled = hasOverflow && selectedIndex > 0;
        NextProviderTabButton.IsEnabled = hasOverflow
            && selectedIndex >= 0
            && selectedIndex < providerCount - 1;
    }

    private void OnProviderTabsViewportSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ProviderTabsClip.Rect = new Windows.Foundation.Rect(
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
        int providerCount = ViewModel.ProviderSummaries.Count;
        double carouselWidth = ProviderTabCarousel.ActualWidth;
        if (providerCount == 0)
        {
            return false;
        }

        int nextPageSize = ProviderTabCarouselLayout.PageSize(providerCount, carouselWidth);
        if (nextPageSize == _providerTabPageSize)
        {
            return false;
        }

        _providerTabPageSize = nextPageSize;
        return true;
    }

    private void UpdateProviderTabLayout()
    {
        if (_visibleProviderTabs.Count == 0 || ProviderTabsViewport.ActualWidth <= 0)
        {
            return;
        }

        double spacing = ProviderTabCarouselLayout.Spacing;
        int count = _visibleProviderTabs.Count;
        _providerTabItemWidth = ProviderTabCarouselLayout.ItemWidth(
            ProviderTabsViewport.ActualWidth,
            count);
        ProviderTabsLayout.Spacing = spacing;
        for (int index = 0; index < count; index++)
        {
            if (ProviderTabsRepeater.TryGetElement(index) is RadioButton tab)
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

    private void PlayProviderTabsTransition(int direction)
    {
        int transitionToken = ++_providerTabsTransitionToken;
        _providerTabsStoryboard?.Stop();
        _providerTabsStoryboard = null;
        ProviderTabsTransitionRoot.Opacity = MotionSettings.ProviderCarouselMinimumOpacity;
        ProviderTabsTransitionTransform.TranslateX = MotionSettings.ProviderCarouselOffset * direction;

        var opacity = new DoubleAnimation
        {
            From = ProviderTabsTransitionRoot.Opacity,
            To = 1,
            Duration = MotionSettings.ProviderCarouselDuration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        var translation = new DoubleAnimation
        {
            From = ProviderTabsTransitionTransform.TranslateX,
            To = 0,
            Duration = MotionSettings.ProviderCarouselDuration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(opacity, ProviderTabsTransitionRoot);
        Storyboard.SetTargetProperty(opacity, nameof(Opacity));
        Storyboard.SetTarget(translation, ProviderTabsTransitionTransform);
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

            storyboard.Stop();
            _providerTabsStoryboard = null;
            ProviderTabsTransitionRoot.Opacity = 1;
            ProviderTabsTransitionTransform.TranslateX = 0;
        };
        _providerTabsStoryboard = storyboard;
        storyboard.Begin();
    }

    private void CancelProviderTabsTransition()
    {
        _providerTabsTransitionToken++;
        _providerTabsStoryboard?.Stop();
        _providerTabsStoryboard = null;
        ProviderTabsTransitionRoot.Opacity = 1;
        ProviderTabsTransitionTransform.TranslateX = 0;
    }
}
