namespace SiloPlayer.Controls;

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
            new PropertyMetadata("", OnAnyChanged));

    public string ImpersonatorUsername
    {
        get => (string)GetValue(ImpersonatorUsernameProperty);
        set => SetValue(ImpersonatorUsernameProperty, value);
    }

    public static readonly DependencyProperty ImpersonatorUsernameProperty =
        DependencyProperty.Register(
            nameof(ImpersonatorUsername),
            typeof(string),
            typeof(ImpersonationBanner),
            new PropertyMetadata("", OnAnyChanged));

    private static void OnAnyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ImpersonationBanner banner)
        {
            banner.UpdateText();
        }
    }

    private void UpdateText()
    {
        // Webui parity (commit 73a8fea): avatar chip + username on the left,
        // "authorized by {admin}" beside it, end-session button on the right.
        ImpersonatingText.Text = string.IsNullOrEmpty(ImpersonatedUsername)
            ? "user"
            : ImpersonatedUsername;
        InitialText.Text = string.IsNullOrEmpty(ImpersonatedUsername)
            ? "?"
            : ImpersonatedUsername[..1].ToUpperInvariant();
        ImpersonatorText.Text = string.IsNullOrEmpty(ImpersonatorUsername)
            ? ""
            : ImpersonatorUsername;
    }

    /// <summary>
    /// Event raised when the user clicks "End Impersonation".
    /// </summary>
    public event EventHandler? EndImpersonationRequested;

    public bool IsEnding
    {
        get => !EndImpersonationButton.IsEnabled;
        set
        {
            EndImpersonationButton.IsEnabled = !value;
            EndSessionText.Text = value ? "Restoring…" : "End session";
        }
    }

    private void EndImpersonation_Click(object sender, RoutedEventArgs e)
    {
        EndImpersonationRequested?.Invoke(this, EventArgs.Empty);
    }
}
