using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;
using A = SiloPlayer.Controls.AdminMonitorUi;
using P = SiloPlayer.Core.Services.Admin.AdminActivityPresentation;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminActivityPage
{
    private LogsDetailSheet? _detailsSheet;
    private string? _detailsSessionId;
    private string? _detailsOpener;
    private Button? _openingButton;

    private void RenderCards(IReadOnlyList<AdminActivitySession> sessions)
    {
        _tableHost.Children.Add(SortRow(cards: true));
        var grid = new Grid { ColumnSpacing = 12, RowSpacing = 12, Margin = new(0, 12, 0, 0) };
        for (var column = 0; column < _cardColumns; column++)
            grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        for (var index = 0; index < sessions.Count; index++)
        {
            if (index % _cardColumns == 0) grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
            var card = StreamCard(sessions[index]);
            Grid.SetColumn(card, index % _cardColumns);
            Grid.SetRow(card, index / _cardColumns);
            grid.Children.Add(card);
        }
        _tableHost.Children.Add(grid);
    }

    private FrameworkElement StreamCard(AdminActivitySession session)
    {
        var state = ObserveSession(session);
        var card = new Grid { Background = A.Brush("CardBackgroundBrush"), CornerRadius = new(12),
            BorderThickness = new(1), BorderBrush = A.Solid(A.Border) };
        var id = "AdminActivityCard:" + session.SessionId;
        AutomationProperties.SetAutomationId(card, id);
        card.RowDefinitions.Add(new() { Height = GridLength.Auto });
        card.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        card.RowDefinitions.Add(new() { Height = GridLength.Auto });

        var header = new Grid();
        // An 8px decoded copy gives the backdrop its soft poster colors. Both
        // decodes share the original artwork download and lease-scoped caches.
        var backdrop = new ImageBrush { Stretch = Stretch.UniformToFill, Opacity = 0.25 };
        var backdropHost = new Border { Background = backdrop, IsHitTestVisible = false };
        AttachPoster(session, backdropHost, backdrop, 8);
        header.Children.Add(backdropHost);

        var top = new Grid { ColumnSpacing = 12, Padding = new(12) };
        top.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        top.Children.Add(Poster(session, stacked: false, card: true));
        var info = new StackPanel { Spacing = 4 };
        var titles = new StackPanel();
        titles.Children.Add(Title(session));
        if (P.GetPlaybackSessionSubtitle(session) is { } subtitle) titles.Children.Add(A.Text(subtitle, 12, A.Muted));
        info.Children.Add(titles);
        info.Children.Add(Viewer(session));
        Grid.SetColumn(info, 1);
        top.Children.Add(info);
        var action = ActionsCell(session, state, compact: true);
        action.VerticalAlignment = VerticalAlignment.Top;
        action.Margin = new(0, -4, -4, 0);
        Grid.SetColumn(action, 2);
        top.Children.Add(action);
        header.Children.Add(top);
        if (P.GetPlaybackProgress(session) is { } progress)
        {
            var bar = RateBar(progress, (state.OptimisticPaused ?? session.IsPaused) ? A.Warning : A.Primary, fill: true,
                width: double.PositiveInfinity, height: 2);
            bar.VerticalAlignment = VerticalAlignment.Bottom;
            AutomationProperties.SetAutomationId(bar, id + ":Progress");
            header.Children.Add(bar);
        }
        card.Children.Add(header);

        var detailsButton = A.Plain(A.Icon("chevron-right", 16, A.Muted), AdminText.T("web.admin.activity.row.details"));
        detailsButton.Width = detailsButton.Height = 28;
        detailsButton.CornerRadius = new(6);
        AutomationProperties.SetAutomationId(detailsButton, "AdminActivityCardOpen:" + session.SessionId);
        ToolTipService.SetToolTip(detailsButton, AdminText.T("web.admin.activity.row.details"));
        detailsButton.Click += (_, _) => OpenCardDetails(session, detailsButton);
        card.Tapped += (_, args) =>
        {
            for (var target = args.OriginalSource as DependencyObject; target != null && !ReferenceEquals(target, card);
                target = VisualTreeHelper.GetParent(target))
                if (target is ButtonBase or TextBox or MenuFlyoutPresenter || target is TextBlock { SelectedText.Length: > 0 }) return;
            OpenCardDetails(session, detailsButton);
        };
        card.PointerEntered += (_, _) => card.BorderBrush = A.Solid(A.Mix(A.Border, A.Foreground, 0.25));
        card.PointerExited += (_, _) => card.BorderBrush = A.Solid(A.Border);

        var facts = new StackPanel { Spacing = 4, Padding = new(12, 10, 12, 10),
            BorderThickness = new(0, 1, 0, 0), BorderBrush = A.Solid(A.Border, 0.6) };
        facts.Children.Add(PlaybackCell(session, state, opener => OpenCardDetails(session, opener), spacing: 4));
        if (BitrateLine(session, withIcon: true) is { } rate) facts.Children.Add(rate);
        if (ShowRoute) facts.Children.Add(RouteBadges(session, showLocation: false));
        Grid.SetRow(facts, 1);
        card.Children.Add(facts);

        var footer = new Grid { ColumnSpacing = 8, Padding = new(12, 6, 6, 6),
            BorderThickness = new(0, 1, 0, 0), BorderBrush = A.Solid(A.Border, 0.6) };
        footer.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var position = ProgressLine(session, state, bar: false);
        position.VerticalAlignment = VerticalAlignment.Center;
        footer.Children.Add(position);
        var elapsed = SessionTime(session);
        Grid.SetColumn(elapsed, 1);
        footer.Children.Add(elapsed);
        Grid.SetColumn(detailsButton, 2);
        footer.Children.Add(detailsButton);
        Grid.SetRow(footer, 2);
        card.Children.Add(footer);
        return card;
    }

    private void OpenCardDetails(AdminActivitySession session, Button opener)
    {
        if (_detailsSheet == null || _lease?.IsCurrent != true) return;
        _detailsSessionId = session.SessionId;
        _detailsOpener = AutomationProperties.GetAutomationId(opener);
        _openingButton = opener;
        RenderCardDetails();
        _openingButton = null;
    }

    private void RenderCardDetails()
    {
        if (_detailsSheet == null || _detailsSessionId == null ||
            _snapshot.Sessions.FirstOrDefault(session => session.SessionId == _detailsSessionId) is not { } session) return;
        var state = ObserveSession(session);
        var title = P.GetPlaybackSessionTitle(session);
        var description = string.Join(" · ", new[] { P.GetPlaybackSessionSubtitle(session), Username(session) }
            .Where(value => !string.IsNullOrEmpty(value)));
        var footer = new Border { BorderBrush = A.Solid(A.Border, 0.7), BorderThickness = new(0, 1, 0, 0),
            Padding = new(16, 10, 16, 10), Child = ActionsCell(session, state, compact: true) };
        var poster = Poster(session, stacked: true);
        var body = ExpandedPanel(session, state, inSheet: true);
        var progress = ProgressLine(session, state);
        if (_detailsSheet.Visibility == Visibility.Visible) _detailsSheet.Update(title, description, body, footer, poster, progress);
        else _detailsSheet.Show(title, description, body, footer, poster, _openingButton, progress);
    }

    internal void CloseCardDetails(bool restoreFocus = true)
    {
        if (!restoreFocus) _detailsOpener = null;
        _detailsSheet?.Close(restoreFocus);
    }

    private void OnCardDetailsClosed()
    {
        if (_detailsSessionId != null && _rowStates.TryGetValue(_detailsSessionId, out var state))
        {
            state.Ffmpeg = false;
            state.Details = false;
        }
        if (_detailsOpener != null) RestoreRowFocus(_detailsOpener);
        _detailsSessionId = null;
        _detailsOpener = null;
    }
}
