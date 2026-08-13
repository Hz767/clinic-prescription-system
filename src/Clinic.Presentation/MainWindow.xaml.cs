using System.Windows;
using Clinic.Presentation.ViewModels;

namespace Clinic.Presentation;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Closed += (_, _) => System.Windows.Application.Current.Shutdown();
    }
}
