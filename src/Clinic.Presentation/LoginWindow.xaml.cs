using System.Windows;
using System.Windows.Controls;
using Clinic.Presentation.Services;
using Clinic.Presentation.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Clinic.Presentation;

public partial class LoginWindow : Window
{
    private readonly IServiceProvider _serviceProvider;

    public LoginWindow(LoginViewModel viewModel, IServiceProvider serviceProvider)
    {
        InitializeComponent();
        DataContext = viewModel;
        _serviceProvider = serviceProvider;
    }

    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is LoginViewModel vm)
            vm.Password = ((PasswordBox)sender).Password;
    }

    private void RegisterLink_Click(object sender, RoutedEventArgs e)
    {
        var windowService = _serviceProvider.GetRequiredService<IWindowService>();
        var regWindow = _serviceProvider.GetRequiredService<DoctorRegistrationWindow>();
        regWindow.Owner = this;
        regWindow.ShowDialog();
    }
}
