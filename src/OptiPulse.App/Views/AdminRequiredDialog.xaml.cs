using System.Windows;

namespace OptiPulse.App.Views;

public partial class AdminRequiredDialog : Window
{
    public AdminRequiredDialog(string title, string explanation)
    {
        InitializeComponent();
        TitleText.Text = title;
        ExplanationText.Text = explanation;
    }

    private void Approve_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
