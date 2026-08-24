using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Core.Models;
using SiloPlayer.Helpers;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Views;

public sealed partial class InviteClaimPage : Page
{
    private InviteClaimNavigation? _navigation;
    public InviteClaimViewModel ViewModel { get; }

    public InviteClaimPage()
    {
        ViewModel = App.Services.GetRequiredService<InviteClaimViewModel>();
        InitializeComponent();
        ViewModel.InvitationAccepted += OnInvitationAccepted;
        ViewModel.PropertyChanged += (_, _) => DispatcherQueue.TryEnqueue(UpdateState);
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is not InviteClaimNavigation navigation)
        {
            ConnectionErrorText.Text = "This invitation link is incomplete.";
            LoadingState.Visibility = Visibility.Collapsed;
            ConnectionErrorState.Visibility = Visibility.Visible;
            return;
        }

        _navigation = navigation;
        await ViewModel.LoadAsync(navigation);
        UpdateState();
        if (ViewModel.Invitation != null) PasswordBox.Focus(FocusState.Programmatic);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        ViewModel.Cancel();
        base.OnNavigatedFrom(e);
    }

    private void UpdateState()
    {
        LoadingState.IsActive = ViewModel.IsLoading;
        LoadingState.Visibility = ViewModel.IsLoading ? Visibility.Visible : Visibility.Collapsed;
        ExpiredState.Visibility = !ViewModel.IsLoading && ViewModel.IsExpired ? Visibility.Visible : Visibility.Collapsed;
        ClaimState.Visibility = !ViewModel.IsLoading && ViewModel.Invitation != null ? Visibility.Visible : Visibility.Collapsed;
        ConnectionErrorState.Visibility = !ViewModel.IsLoading && !ViewModel.IsExpired &&
            ViewModel.Invitation == null ? Visibility.Visible : Visibility.Collapsed;
        if (ViewModel.Invitation is { } invitation)
        {
            EmailBox.Text = invitation.Email;
            WelcomeTitle.Text = $"Welcome to {invitation.ServerName}";
            InviterText.Text = string.IsNullOrWhiteSpace(invitation.InviterName)
                ? ""
                : $"INVITED BY {invitation.InviterName}";
            InviterText.Visibility = string.IsNullOrWhiteSpace(invitation.InviterName)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }
        ConnectionErrorText.Text = ViewModel.ErrorMessage ?? "The invitation could not be loaded.";
        CreateAccountButton.Content = ViewModel.IsSubmitting ? "Creating account…" : "Create account";
    }

    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        ViewModel.Password = PasswordBox.Password;
        UpdateMismatch();
    }

    private void ConfirmPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        ViewModel.ConfirmPassword = ConfirmPasswordBox.Password;
        UpdateMismatch();
    }

    private void UpdateMismatch() => MismatchText.Visibility = ConfirmPasswordBox.Password.Length > 0 &&
        !string.Equals(PasswordBox.Password, ConfirmPasswordBox.Password, StringComparison.Ordinal)
        ? Visibility.Visible : Visibility.Collapsed;

    private void Input_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter && ViewModel.AcceptCommand.CanExecute(null))
            ViewModel.AcceptCommand.Execute(null);
    }

    private async void Retry_Click(object sender, RoutedEventArgs e)
    {
        if (_navigation != null) await ViewModel.LoadAsync(_navigation);
        UpdateState();
    }

    private void SignIn_Click(object sender, RoutedEventArgs e)
    {
        if (_navigation == null)
        {
            App.Services.GetRequiredService<NavigationService>().Navigate<ServerSelectPage>();
            return;
        }
        App.Services.GetRequiredService<NavigationService>().Navigate<LoginPage>(new ServerEntry
        {
            Url = _navigation.ServerUrl,
            Name = ViewModel.Invitation?.ServerName ?? "Silo",
        });
    }

    private void OnInvitationAccepted()
        => DispatcherQueue.TryEnqueue(() =>
            App.Services.GetRequiredService<NavigationService>().Navigate<HouseholdSetupPage>());
}
