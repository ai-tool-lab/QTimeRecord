using System.Windows;
using QTimeRecord.App.ViewModels;

namespace QTimeRecord.App;

/// <summary>
/// シェル。画面の入れ物であり、ここにロジックは書かない。
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow(ShellViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
