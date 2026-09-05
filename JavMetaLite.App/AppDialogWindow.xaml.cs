using System.Windows;

namespace JavMetaLite.App;

public partial class AppDialogWindow : Window
{
    public AppDialogWindow(
        string title,
        string message,
        string confirmText,
        bool showCancel = true,
        bool destructive = true)
    {
        InitializeComponent();
        Title = title;
        DialogHeadingText.Text = title;
        DialogMessageText.Text = message;
        ConfirmButton.Content = confirmText;
        CancelButton.Visibility = showCancel ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.Margin = showCancel ? new Thickness(0, 0, 10, 0) : new Thickness(0);
        CancelButton.IsDefault = destructive && showCancel;
        ConfirmButton.IsDefault = !destructive || !showCancel;
        if (!destructive)
        {
            ConfirmButton.Style = FindResource("PrimaryButton") as Style;
        }
        Loaded += (_, _) =>
        {
            if (destructive && showCancel)
            {
                CancelButton.Focus();
            }
            else
            {
                ConfirmButton.Focus();
            }
        };
        WindowVisualTheme.ApplyDarkTitleBar(this);
    }

    private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
