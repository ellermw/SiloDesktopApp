using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Text;
using Microsoft.Extensions.DependencyInjection;
using SiloPlayer.Core.Api;

namespace SiloPlayer.Helpers;

public static class AuthFormLayout
{
    public static FrameworkElement Create(StackPanel form, bool showBrand = true, double maxWidth = 448, double contentInset = 0, double radius = 32, double verticalPadding = 28, double borderThickness = 1)
    {
        form.MaxWidth = double.PositiveInfinity;
        form.Margin = new Thickness(0);
        form.HorizontalAlignment = HorizontalAlignment.Stretch;
        var brand = new TextBlock { Text = "Silo", FontSize = 30, FontWeight = FontWeights.ExtraBold };
        if (showBrand) form.Children.Insert(0, brand);
        var card = new Border { Width = maxWidth, MaxWidth = maxWidth, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(24),
            CornerRadius = new CornerRadius(radius), Padding = new Thickness(28 + contentInset, verticalPadding, 28 + contentInset, verticalPadding),
            Background = (Brush)Application.Current.Resources["CardBackgroundBrush"], BorderBrush = (Brush)Application.Current.Resources["BorderBrush"], BorderThickness = new Thickness(borderThickness), Child = form };
        var shell = new Grid();
        AuthBackdrop.SetEnabled(shell, true);
        shell.SizeChanged += (_, e) => card.Width = Math.Min(maxWidth, Math.Max(1, e.NewSize.Width - 48));
        shell.Children.Add(new ScrollViewer { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Center, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = card });
        if (showBrand) shell.Loaded += async (_, _) =>
        {
            try { var branding = await App.Services.GetRequiredService<SettingsApi>().GetServerBrandingAsync(); brand.Text = branding.ServerName ?? "Silo"; }
            catch { }
        };
        return shell;
    }
}
