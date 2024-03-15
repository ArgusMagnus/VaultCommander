using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace VaultCommander;

/// <summary>
/// Interaction logic for PasswordDialog.xaml
/// </summary>
sealed partial class PasswordDialog : Window
{
    PasswordDialog()
    {
        InitializeComponent();
    }

    private void OnButtonOK(object sender, RoutedEventArgs e) => DialogResult = true;

    public static (string? UserEmail, EncryptedString? Password) Show(Window owner, string? userEmail, bool emailOnly = false)
    {
        PasswordDialog dlg = new() { Owner = owner };
        if (!string.IsNullOrEmpty(userEmail))
        {
            dlg._emailBox.Text = userEmail;
            dlg._emailBox.IsReadOnly = true;
        }
        if (emailOnly)
        {
            dlg._passwordLabel.Visibility = Visibility.Collapsed;
            dlg._passwordBox.Visibility = Visibility.Collapsed;
        }
        return dlg.ShowDialog() is true ? (dlg._emailBox.Text, new(dlg._passwordBox.Password)) : default;
    }

    private void OnUseSsoChanged(object sender, RoutedEventArgs e)
    {
        var cb = (CheckBox)sender;
        _passwordBox.IsEnabled = cb.IsChecked is not true;
    }
}
