using JustMeetinPoint.Maui.Features.Auth.ViewModels;

namespace JustMeetinPoint.Maui.Features.Auth.Views;

public partial class LoginView : ContentPage
{
    public LoginView(LoginViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}