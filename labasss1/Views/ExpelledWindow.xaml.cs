using System.Windows;
using System.Windows.Input;
using labasss1.Models;
using labasss1.ViewModels;

namespace labasss1.Views;

public partial class ExpelledWindow : Window
{
    public ExpelledWindow()
    {
        InitializeComponent();
    }

    private MainViewModel ViewModel => (MainViewModel)DataContext;

    private void Delete_Click(object sender, RoutedEventArgs e) => DeleteSelected();

    private void ExpelledGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete) return;
        DeleteSelected();
        e.Handled = true;
    }

    /// <summary>Удаляет выделенных студентов безвозвратно — только после подтверждения.</summary>
    private void DeleteSelected()
    {
        var selected = ExpelledGrid.SelectedItems.OfType<Student>().ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show(this, "Выделите студентов, которых нужно удалить.", Title,
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var question = selected.Count == 1
            ? $"Удалить студента «{selected[0].FullName}» безвозвратно?"
            : $"Удалить выделенных студентов ({selected.Count}) безвозвратно?";
        var answer = MessageBox.Show(this, question + "\nЭто действие нельзя отменить.", "Подтверждение удаления",
            MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer == MessageBoxResult.Yes)
            ViewModel.DeleteCommand.Execute(selected);
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        ExpelledGrid.SelectAll();
        ExpelledGrid.Focus();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
