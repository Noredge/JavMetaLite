using System.Windows;

namespace JavMetaLite.App;

public partial class DiscoveryDetailsWindow : Window
{
    public DiscoveryDetailsWindow(string details, string? title = null)
    {
        InitializeComponent();
        DetailsText.Text = details;
        if (title is not null) { Title = title; HeadingText.Text = title; }
        WindowVisualTheme.ApplyDarkTitleBar(this);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
