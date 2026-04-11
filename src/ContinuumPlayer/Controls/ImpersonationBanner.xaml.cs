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
        // Match WebUI format: "Impersonating {user} as requested by {impersonator}".
        // Fall back gracefully when fields are missing.
        if (string.IsNullOrEmpty(ImpersonatedUsername))
        {
            ImpersonatingText.Text = "Impersonating user";
        }
        else if (string.IsNullOrEmpty(ImpersonatorUsername))
        {
            ImpersonatingText.Text = $"Impersonating {ImpersonatedUsername}";
        }
        else
        {
            ImpersonatingText.Text = $"Impersonating {ImpersonatedUsername} as requested by {ImpersonatorUsername}";
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
