using Microsoft.UI.Xaml.Media;

namespace SiloPlayer.Controls;

/// <summary>
/// AudiobookDetailSkeleton.tsx: holds the book detail layout while an
/// audiobook or ebook loads (the same grid, cover aspect, actions, header
/// lines, facts row, overview and, for audiobooks, chapter list) so nothing
/// moves when the book arrives. The grid follows audiobooks.css: a side
/// column from lg for audiobooks and from md for ebooks, the cover beside the
/// head below that.
/// </summary>
public sealed class BookDetailSkeleton : Grid
{
    private readonly bool _ebook;
    private readonly StackPanel _side = new() { Spacing = 16, VerticalAlignment = VerticalAlignment.Top };
    private readonly SkeletonBox _cover = new();
    private readonly StackPanel _actions = new() { Spacing = 12 };
    private readonly StackPanel _head = new() { Spacing = 6 };
    private readonly StackPanel _body = new() { Spacing = 40 };
    private readonly SkeletonBox _title = new();
    private readonly SkeletonBox _series = Line(176, 20);
    private readonly SkeletonBox _author = Line(192, 20);
    private readonly SkeletonBox? _narrator;
    private readonly SkeletonBox _primary = Line(double.NaN, 40, 20);
    private readonly SkeletonBox _secondaryWide = Line(double.NaN, 40, 20);
    private readonly SkeletonBox _secondaryNarrow = Line(96, 40, 20);
    private readonly Grid _secondaryRow = new() { ColumnSpacing = 8 };
    private readonly WrapPanel _facts = new() { HorizontalSpacing = 36, VerticalSpacing = 12 };
    private readonly List<StackPanel> _factCells = [];
    private readonly StackPanel _chapters = new() { Spacing = 12 };
    private readonly StackPanel _overview = new() { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly SkeletonBox _overviewLast = Line(0, 16, 6);

    public BookDetailSkeleton(bool ebook)
    {
        _ebook = ebook;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _cover.HorizontalAlignment = HorizontalAlignment.Left;
        _cover.VerticalAlignment = VerticalAlignment.Bottom;

        _actions.Children.Add(_primary);
        _secondaryRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _secondaryRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (ebook)
        {
            _secondaryRow.Children.Add(_secondaryWide);
            _secondaryRow.Children.Add(_secondaryNarrow);
            Grid.SetColumn(_secondaryNarrow, 1);
        }
        else
        {
            var first = Line(36, 36, 18); var second = Line(36, 36, 18);
            first.HorizontalAlignment = second.HorizontalAlignment = HorizontalAlignment.Left;
            _secondaryRow.ColumnDefinitions[0].Width = GridLength.Auto;
            _secondaryRow.ColumnDefinitions[1].Width = GridLength.Auto;
            _secondaryRow.Children.Add(first);
            _secondaryRow.Children.Add(second);
            Grid.SetColumn(second, 1);
        }
        _actions.Children.Add(_secondaryRow);
        _side.Children.Add(_cover);

        _title.CornerRadius = new CornerRadius(8);
        _title.HorizontalAlignment = HorizontalAlignment.Left;
        _head.Children.Add(_series);
        _head.Children.Add(_title);
        _head.Children.Add(_author);
        if (!ebook)
        {
            _narrator = Line(176, 20);
            _head.Children.Add(_narrator);
        }

        var about = new StackPanel { Spacing = 24 };
        foreach (var (labelWidth, valueWidth) in new[] { (48d, 64d), (48d, 56d), (48d, 48d), (48d, 96d) })
        {
            var cell = new StackPanel { Spacing = 6 };
            cell.Children.Add(Line(labelWidth, 14));
            var value = Line(valueWidth, 20);
            cell.Children.Add(value);
            _facts.Children.Add(cell);
            _factCells.Add(cell);
        }
        about.Children.Add(new Border
        {
            Padding = new Thickness(0, 16, 0, 16),
            BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(0, 1, 0, 1),
            Child = _facts,
        });
        _overview.Children.Add(Line(double.NaN, 16, 6));
        _overview.Children.Add(Line(double.NaN, 16, 6));
        _overview.Children.Add(_overviewLast);
        about.Children.Add(_overview);
        var genres = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var width in new[] { 64d, 80d, 56d }) genres.Children.Add(Line(width, 24, 12));
        about.Children.Add(genres);
        _body.Children.Add(about);

        if (!ebook)
        {
            _chapters.Spacing = 12;
            _chapters.Children.Add(Line(112, 28, 6));
            var rows = new StackPanel { Spacing = 2 };
            for (var i = 0; i < 6; i++)
            {
                var row = new Grid { Padding = new Thickness(12, 10, 12, 10), ColumnSpacing = 16 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var number = Line(16, 12, 6); number.HorizontalAlignment = HorizontalAlignment.Right;
                var title = Line(double.NaN, 16, 6); title.HorizontalAlignment = HorizontalAlignment.Left; title.Width = 260;
                var time = Line(40, 12, 6);
                row.Children.Add(number);
                row.Children.Add(title); SetColumn(title, 1);
                row.Children.Add(time); SetColumn(time, 2);
                rows.Children.Add(row);
            }
            _chapters.Children.Add(rows);
            _body.Children.Add(_chapters);
        }

        Children.Add(_side);
        Children.Add(_head);
        Children.Add(_body);
        ApplyWidth(1280);
    }

    private static SkeletonBox Line(double width, double height, double radius = 6)
    {
        var box = new SkeletonBox { Height = height, CornerRadius = new CornerRadius(radius), HorizontalAlignment = HorizontalAlignment.Left };
        if (!double.IsNaN(width)) box.Width = width;
        else box.HorizontalAlignment = HorizontalAlignment.Stretch;
        return box;
    }

    /// <summary>The audiobooks.css grid at a page width: the side column, or the stacked cover-and-head grid.</summary>
    public void ApplyWidth(double width)
    {
        if (width <= 0) return;
        var large = width >= 1024;
        var small = width >= 640;
        var twoColumn = large || _ebook && width >= 768;
        // grid-template-rows: auto 1fr; the tall side column must not stretch the head's row.
        RowDefinitions[1].Height = twoColumn ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
        var middle = twoColumn && !large;
        var gutter = width >= 1280 ? 48d : width >= 1024 ? 40d : small ? 24d : 16d;
        Margin = new Thickness(gutter, large ? 96 : small ? 80 : 64, gutter, 56);
        MaxWidth = Math.Max(0, 1520 - gutter * 2);
        ColumnSpacing = large ? 56 : middle ? 40 : small ? 32 : 16;
        RowSpacing = large ? 32 : middle ? 28 : small ? 24 : 20;
        var cover = large ? 260d : middle ? 216d : small ? 176d : 112d;
        ColumnDefinitions[0].Width = new GridLength(cover);
        _side.Width = cover;
        _cover.Width = cover;
        _cover.Height = _ebook ? cover * 1.5 : cover;
        _cover.CornerRadius = new CornerRadius(large ? 16 : 12);

        _secondaryWide.Height = _secondaryNarrow.Height = _ebook && twoColumn ? 36 : 40;
        _secondaryWide.CornerRadius = _secondaryNarrow.CornerRadius = new CornerRadius(_secondaryWide.Height / 2);

        if (twoColumn)
        {
            if (!_side.Children.Contains(_actions))
            {
                Children.Remove(_actions);
                _side.Children.Add(_actions);
            }
            SetRow(_side, 0); SetColumn(_side, 0); SetRowSpan(_side, 2);
            SetRow(_head, 0); SetColumn(_head, 1); SetColumnSpan(_head, 1);
            SetRow(_body, 1); SetColumn(_body, 1); SetColumnSpan(_body, 1);
            _head.VerticalAlignment = VerticalAlignment.Top;
            _head.Padding = new Thickness(0, large ? 12 : _ebook ? 4 : 0, 0, 0);
        }
        else
        {
            if (_side.Children.Contains(_actions))
            {
                _side.Children.Remove(_actions);
                Children.Add(_actions);
            }
            SetRow(_side, 0); SetColumn(_side, 0); SetRowSpan(_side, 1);
            SetRow(_actions, 1); SetColumn(_actions, 0); SetColumnSpan(_actions, 2);
            SetRow(_head, 0); SetColumn(_head, 1); SetColumnSpan(_head, 1);
            SetRow(_body, 2); SetColumn(_body, 0); SetColumnSpan(_body, 2);
            _head.VerticalAlignment = VerticalAlignment.Bottom;
            _head.Padding = new Thickness(0);
        }
        _head.Spacing = large ? 8 : 6;
        _body.Spacing = large ? 48 : 40;
        _series.Height = 20;
        _title.Height = large ? 52 : small ? 40 : 28;
        _title.Width = Math.Max(0, Math.Min(512, width - gutter * 2 - cover - ColumnSpacing));
        _title.Margin = new Thickness(0, 0, 0, large ? 4 : 0);
        _author.Height = small ? 24 : 20;
        if (_narrator != null) _narrator.Height = small ? 24 : 20;

        // max-w-[68ch] overview: two full lines and one at 80%.
        var bodyWidth = twoColumn ? width - gutter * 2 - cover - ColumnSpacing : width - gutter * 2;
        _overview.Width = Math.Max(0, Math.Min(620, bodyWidth));
        _overviewLast.Width = _overview.Width * .8;

        // grid-cols-2 below sm, a wrapping row from sm.
        _facts.HorizontalSpacing = small ? 36 : 24;
        var half = Math.Max(0, (width - gutter * 2 - 24) / 2);
        foreach (var cell in _factCells) cell.Width = small ? double.NaN : half;
    }
}
