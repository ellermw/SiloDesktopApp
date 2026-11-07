using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using Windows.System;

namespace SiloPlayer.Views;

public sealed partial class LibraryPage
{
    private const string LibraryLetters = "#ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private readonly Dictionary<string, Button> _libraryLetterButtons = new(StringComparer.Ordinal);
    private CatalogLettersResponse? _displayedLetterIndex;
    private string? _focusedLibraryLetter;
    private string? _activeLibraryLetter;
    private string? _chosenLibraryEndLetter;

    private bool IsLibraryLetterGrid => _isNavigated && _currentTab == "Library" &&
        _currentAudiobookAxis == "books" && ViewModel.SelectedSort == "title";

    private bool CanShowLibraryLetters => IsLibraryLetterGrid &&
        ViewModel.LetterIndex is { Total: > 0, Letters.Count: > 0 };

    private void InitializeLibraryLetters()
    {
        foreach (var character in LibraryLetters)
        {
            var letter = character.ToString();
            var button = new Button
            {
                Content = letter, Tag = letter, Width = 24, MinWidth = 0, MinHeight = 0,
                Padding = new Thickness(0), FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(12),
                IsTabStop = false, IsEnabled = false,
            };
            button.Click += LibraryLetter_Click;
            button.GotFocus += (_, _) =>
            {
                _focusedLibraryLetter = letter;
                UpdateLibraryLetterTabStop();
            };
            AutomationProperties.SetAutomationId(button, "LibraryLetter" + (letter == "#" ? "Other" : letter));
            _libraryLetterButtons.Add(letter, button);
            LibraryLetterButtons.Children.Add(button);
        }
    }

    private void LibraryLetters_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ViewModel.LetterIndex) or nameof(ViewModel.IsLetterIndexLoading) or nameof(ViewModel.SelectedSort) or
            nameof(ViewModel.SelectedOrder) or nameof(ViewModel.Library) or nameof(ViewModel.TotalCount))
            DispatcherQueue.TryEnqueue(UpdateLibraryLetters);
    }

    private void UpdateLibraryLetters()
    {
        if (!ReferenceEquals(_displayedLetterIndex, ViewModel.LetterIndex))
        {
            _displayedLetterIndex = ViewModel.LetterIndex;
            _chosenLibraryEndLetter = null;
        }
        LibraryLetterRail.Visibility = CanShowLibraryLetters ? Visibility.Visible : Visibility.Collapsed;
        foreach (var (letter, button) in _libraryLetterButtons)
        {
            var entry = CanShowLibraryLetters ? ViewModel.LetterIndex?.Letters.FirstOrDefault(item => item.Letter == letter) : null;
            button.IsEnabled = entry is { Count: > 0 } && entry.Position < ViewModel.TotalCount;
            var spoken = letter == "#" ? "Numbers and symbols" : letter;
            var count = entry?.Count ?? 0;
            AutomationProperties.SetName(button, $"{spoken}, {(count == 0 ? "no titles" : count == 1 ? "1 title" : $"{count:N0} titles")}");
        }
        if (_chosenLibraryEndLetter is not null && !_libraryLetterButtons[_chosenLibraryEndLetter].IsEnabled)
            _chosenLibraryEndLetter = null;
        UpdateLibraryLetterLayout();
        UpdateActiveLibraryLetter(force: true);
    }

    private void LibraryContentArea_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateLibraryLetterLayout();

    private void UpdateLibraryLetterLayout()
    {
        // Keep the same width from pending to loaded, but release the gutter
        // for unsupported, failed or empty indexes so ordinary browsing uses
        // the full viewport.
        var available = Math.Max(14, LibraryContentArea.ActualHeight - 88);
        var columns = available < 540 ? 2 : 1;
        var reserveRail = IsLibraryLetterGrid && (ViewModel.IsLetterIndexLoading || CanShowLibraryLetters);
        LibraryLetterColumn.Width = new GridLength(reserveRail ? columns * 24 + 18 : 0);
        if (LibraryLetterRail.Visibility != Visibility.Visible) return;
        // Keep all 27 targets inside the space below the toolbar, including on
        // short windows. Fill down each column, matching the WebUI's order.
        var rows = columns == 2 ? 14 : 27;
        LibraryLetterRail.Height = Math.Min(672, available + 14);
        if (LibraryLetterButtons.RowDefinitions.Count == rows) return;
        LibraryLetterButtons.ColumnDefinitions.Clear();
        LibraryLetterButtons.RowDefinitions.Clear();
        for (var column = 0; column < columns; column++)
            LibraryLetterButtons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (var row = 0; row < rows; row++)
            LibraryLetterButtons.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        for (var index = 0; index < LibraryLetters.Length; index++)
        {
            var button = _libraryLetterButtons[LibraryLetters[index].ToString()];
            Grid.SetRow(button, index % rows);
            Grid.SetColumn(button, index / rows);
        }
    }

    private void UpdateActiveLibraryLetter(bool force = false)
    {
        if (!CanShowLibraryLetters)
        {
            _activeLibraryLetter = null;
            UpdateLibraryLetterTabStop();
            return;
        }
        var layout = GetGridLayout();
        var maxOffset = VirtualGridScrollGate.GetMaxVerticalOffset(
            ViewModel.TotalCount, layout.Columns, layout.RowHeight, GetLibraryViewportHeight());
        var atBottom = Math.Abs(_libraryVerticalOffset - maxOffset) < 2;
        if (!atBottom) _chosenLibraryEndLetter = null;
        // Native scrolling rounds fractional row boundaries to rendered pixels.
        // Ignore a subpixel sliver of the preceding row when naming the top letter.
        var letterRow = VirtualGridScrollGate.GetFirstVisibleRow(_libraryVerticalOffset + .5, layout.RowHeight);
        var topRowEnd = Math.Min(ViewModel.TotalCount - 1, (letterRow + 1) * layout.Columns - 1);
        var activeLetter = atBottom && _chosenLibraryEndLetter is not null
            ? _chosenLibraryEndLetter : ViewModel.LetterIndex!.LetterAtIndex(topRowEnd);
        if (!force && activeLetter == _activeLibraryLetter) return;
        _activeLibraryLetter = activeLetter;
        foreach (var (letter, button) in _libraryLetterButtons)
        {
            var active = letter == _activeLibraryLetter;
            button.Background = active ? (Brush)Application.Current.Resources["AccentBrush"] : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            button.Foreground = (Brush)Application.Current.Resources[active ? "PrimaryTextBrush" : "SecondaryTextBrush"];
            button.Opacity = button.IsEnabled ? 1 : .35;
            AutomationProperties.SetHelpText(button, active ? "Current letter" : "Jump to letter");
        }
        UpdateLibraryLetterTabStop();
    }

    private void UpdateLibraryLetterTabStop()
    {
        var tabStop = new[] { _focusedLibraryLetter, _activeLibraryLetter }
            .FirstOrDefault(letter => letter is not null && _libraryLetterButtons[letter].IsEnabled)
            ?? _libraryLetterButtons.FirstOrDefault(pair => pair.Value.IsEnabled).Key;
        foreach (var (letter, button) in _libraryLetterButtons)
            button.IsTabStop = CanShowLibraryLetters && button.IsEnabled && letter == tabStop;
    }

    private async void LibraryLetter_Click(object sender, RoutedEventArgs e)
    {
        if (CanShowLibraryLetters && sender is Button { IsEnabled: true, Tag: string letter })
            await ViewModel.JumpToLetterCommand.ExecuteAsync(letter);
    }

    private void OnLibraryLetterJumpRequested(int index)
    {
        var owner = ViewModel.LetterIndex;
        var catalog = App.Services.GetRequiredService<CatalogApi>();
        var context = catalog.CaptureContext();
        var language = catalog.FilterCacheGeneration;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!CanShowLibraryLetters || !ReferenceEquals(owner, ViewModel.LetterIndex) ||
                index >= ViewModel.TotalCount || ViewModel.GetWindowItem(index) is null ||
                !catalog.IsCurrentContext(context) || catalog.FilterCacheGeneration != language) return;
            UpdateVirtualGridMetrics();
            var layout = GetGridLayout();
            var offset = (index / layout.Columns) * layout.RowHeight;
            var maxOffset = VirtualGridScrollGate.GetMaxVerticalOffset(
                ViewModel.TotalCount, layout.Columns, layout.RowHeight, GetLibraryViewportHeight());
            _chosenLibraryEndLetter = offset >= maxOffset ? owner?.LetterAtIndex(index) : null;
            SetLibraryVerticalOffset(offset, force: true);
            UpdateActiveLibraryLetter();
        });
    }

    private void LibraryLetterButtons_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!CanShowLibraryLetters || IsLibraryLetterModifierDown(VirtualKey.Control) ||
            IsLibraryLetterModifierDown(VirtualKey.Menu) || IsLibraryLetterModifierDown(VirtualKey.LeftWindows) ||
            IsLibraryLetterModifierDown(VirtualKey.RightWindows)) return;
        var enabled = _libraryLetterButtons.Where(pair => pair.Value.IsEnabled).Select(pair => pair.Key).ToArray();
        if (enabled.Length == 0) return;
        var current = e.OriginalSource is Button { Tag: string letter } ? letter : _focusedLibraryLetter ?? _activeLibraryLetter;
        var at = Math.Max(0, Array.IndexOf(enabled, current));
        var key = (int)e.Key;
        string? next = e.Key switch
        {
            VirtualKey.Down or VirtualKey.Right => enabled[Math.Min(at + 1, enabled.Length - 1)],
            VirtualKey.Up or VirtualKey.Left => enabled[Math.Max(at - 1, 0)],
            VirtualKey.Home => enabled[0],
            VirtualKey.End => enabled[^1],
            _ => key is >= 65 and <= 90 ? ((char)key).ToString()
                : key is >= 48 and <= 57 or >= 96 and <= 105 or >= 186 and <= 222 ? "#" : null,
        };
        // Native Button handles Enter and Space activation; navigation never
        // invokes a jump or captures wheel/pointer input from the catalog.
        if (next is null || !enabled.Contains(next)) return;
        e.Handled = true;
        _focusedLibraryLetter = next;
        UpdateLibraryLetterTabStop();
        _libraryLetterButtons[next].Focus(FocusState.Keyboard);
    }

    private static bool IsLibraryLetterModifierDown(VirtualKey key) =>
        Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
}
