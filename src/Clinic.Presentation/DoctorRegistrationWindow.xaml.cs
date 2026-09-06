using System.Windows;
using Clinic.Presentation.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Clinic.Presentation;

public partial class DoctorRegistrationWindow : Window
{
    private readonly DoctorRegistrationViewModel _vm;

    public DoctorRegistrationWindow(IServiceProvider serviceProvider)
    {
        InitializeComponent();
        _vm = new DoctorRegistrationViewModel(serviceProvider);
        DataContext = _vm;
    }

    private void PwdBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        _vm.Password = PwdBox.Password;
    }

    private void ConfirmPwdBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        _vm.ConfirmPassword = ConfirmPwdBox.Password;
    }
}
