using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AppStart.Setup.ViewModels;

namespace AppStart.Setup.Views;

/// <summary>
/// Wizard content. Code-behind only for what cannot be bound: PasswordBox.SecurePassword (not bindable by
/// design) and Enter in the member field.
/// </summary>
public partial class WizardView : UserControl
{
    public WizardView()
    {
        InitializeComponent();
    }

    private WizardViewModel? ViewModel => DataContext as WizardViewModel;

    private void OnPasswordChanged(object sender, RoutedEventArgs e) =>
        ViewModel?.SetPassword(PasswordBox1.SecurePassword);

    private void OnPasswordConfirmChanged(object sender, RoutedEventArgs e) =>
        ViewModel?.SetPasswordConfirm(PasswordBox2.SecurePassword);

    private void OnMemberKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ViewModel?.AddMemberCommand.CanExecute(null) == true)
        {
            ViewModel.AddMemberCommand.Execute(null);
            e.Handled = true;
        }
    }
}
