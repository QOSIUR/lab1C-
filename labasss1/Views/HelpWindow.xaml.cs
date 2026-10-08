using System.Windows;

namespace labasss1.Views;

/// <summary>Окно справки: описание всех функций программы по разделам.</summary>
public partial class HelpWindow : Window
{
    public HelpWindow()
    {
        InitializeComponent();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
