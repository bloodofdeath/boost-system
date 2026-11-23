using System.Windows;
using OptiPulse.App.ViewModels;

namespace OptiPulse.App.Views;

public partial class NotRecommendedDialog : Window
{
    public NotRecommendedDialog(TweakCardViewModel card)
    {
        InitializeComponent();
        DataContext = card;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
