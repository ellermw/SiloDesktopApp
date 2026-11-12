using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Controls;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Views.Dialogs;

public sealed partial class ProfileEditorDialog
{
    private FrameworkElement BuildAllowanceManagement()
    {
        var host = new StackPanel { Visibility = Visibility.Collapsed, Spacing = 8 };
        if (_profile == null || _profile.IsPrimary || !TitleAllowanceSession.HasLimit(_profile)) return host;
        var open = false;
        var owner = AllowanceDialogOwner.Capture(() => open);
        if (!owner.IsPrimary || _profile.Id == owner.Owner.ProfileId) return host;
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var retry = new Button { Content = "Retry title allowances", Visibility = Visibility.Collapsed };
        host.Children.Add(error); host.Children.Add(retry);
        async Task LoadAsync()
        {
            try
            {
                if (!await owner.LoadAsync()) { if (owner.IsCurrent) host.Visibility = Visibility.Collapsed; return; }
                if (!owner.IsCurrent) return;
                error.Text = ""; retry.Visibility = Visibility.Collapsed;
                host.Visibility = Visibility.Visible;
                var section = new AllowedTitlesSection(owner, _profile.Id);
                while (host.Children.Count > 2) host.Children.RemoveAt(2);
                host.Children.Add(section); await section.LoadAsync();
            }
            catch (OperationCanceledException) { }
            catch { if (owner.IsCurrent) { host.Visibility = Visibility.Visible; error.Text = "Couldn’t load title allowance access."; retry.Visibility = Visibility.Visible; } }
        }
        _dialog.Opened += async (_, _) => { open = true; await LoadAsync(); };
        _dialog.Closed += (_, _) => open = false;
        retry.Click += async (_, _) => await LoadAsync();
        return host;
    }
}
