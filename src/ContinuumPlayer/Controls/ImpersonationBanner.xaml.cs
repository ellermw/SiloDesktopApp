namespace ContinuumPlayer.Controls;

public sealed partial class ImpersonationBanner : UserControl
{
    public ImpersonationBanner()
    {
        this.InitializeComponent();
    }

    public string ImpersonatedUsername
    {
        get => (string)GetValue(ImpersonatedUsernameProperty);
        set => SetValue(ImpersonatedUsernameProperty, value);
    }

    public static readonly DependencyProperty ImpersonatedUsernameProperty =
        DependencyProperty.Register(
            nameof(ImpersonatedUsername),
            typeof(string),
            typeof(ImpersonationBanner),
            new PropertyMetadata("", OnUsernameChanged));

    private static void OnUsernameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ImpersonationBanner banner)
        {
            var username = e.NewValue as string ?? "";
            banner.ImpersonatingText.Text = string.IsNullOrEmpty(username)
                ? "Impersonating user"
                : $"Impersonating {username}";
        }
    }

    /// <summary>
    /// Event raised when the user clicks "End Impersonation".
    /// </summary>
    public event EventHandler? EndImpersonationRequested;

    private void EndImpersonation_Click(object sender, RoutedEventArgs e)
    {
        EndImpersonationRequested?.Invoke(this, EventArgs.Empty);
    }
}
