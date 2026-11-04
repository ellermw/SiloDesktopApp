using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Services.PlaybackMenus;

namespace SiloPlayer.Services;

public partial class PlayerService
{
    private PlaybackChoiceMenu CreateNativeStatsMenu(NativeMenuOwner owner, PlaybackMenuRequest request,
        Action<PlaybackMenuCloseReason> finish) =>
        new(owner.Video, request.Anchor, "Playback info",
            [new PlaybackMenuChoice("collecting", "Playback information", "Collecting current playback details…", [], IsEnabled: false)],
            () => owner.IsCurrent() && _nativeMenuGate.IsCurrent(request), _ => { }, finish,
            root => App.Services.GetService<AccessibilityService>()?.ApplySaved(root),
            choiceContent: CreateNativeStatsRow, dismissKey: Windows.System.VirtualKey.I);

    private static FrameworkElement CreateNativeStatsRow(PlaybackMenuChoice choice)
    {
        var content = new StackPanel { Margin = new Thickness(4, 5, 4, 5), Spacing = 5 };
        var section = choice.Badges.FirstOrDefault() ?? "";
        var startsSection = (section, choice.Title) is ("PLAYER", "Player") or ("VIDEO INFO", "Display area") or
            ("PLAYBACK STREAM INFO", "Video codec") or ("CURRENT SOURCE FILE", "Container");
        if (startsSection)
            content.Children.Add(new TextBlock { Text = section, FontSize = 11, Opacity = .7,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 4) });
        var line = new Grid { ColumnSpacing = 12 };
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        line.Children.Add(new TextBlock { Text = choice.Title, TextWrapping = TextWrapping.Wrap, FontSize = 13, Opacity = .7 });
        var value = new TextBlock { Text = choice.Metadata, TextWrapping = TextWrapping.Wrap, FontSize = 13 };
        Grid.SetColumn(value, 1); line.Children.Add(value); content.Children.Add(line);
        return content;
    }

    private void HandleNativeStatsData(string token, string sequence, string json)
    {
        var owner = _nativeMenuOwner;
        var request = _nativeMenuRequest;
        if (owner == null || !owner.IsCurrent() || owner.Token != token || request?.Kind != "stats" ||
            !long.TryParse(sequence, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ||
            request.Sequence != number || !_nativeMenuGate.IsCurrent(request) || _nativeMenu == null) return;
        var rows = PlaybackStatsSnapshot.Parse(json);
        if (rows == null || rows.Count == 0) return;
        // Read-only rows remain focusable for native keyboard scrolling and full
        // accessible labels. Invoking one never closes the panel or runs a command.
        _nativeMenu.UpdateChoices(rows.Select(row => new PlaybackMenuChoice(row.Id, row.Label,
            row.Value, [row.Section], CloseOnInvoke: false)).ToArray());
    }
}
