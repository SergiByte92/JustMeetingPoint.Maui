using Microsoft.Maui.Controls;

namespace JustMeetinPoint.Maui.Features.Auth.Views
{
    public partial class LoginView : ContentPage
    {
        public LoginView()
        {
            InitializeComponent();
        }

        private async void OnRegisterClicked(object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync("///register");
        }

        private async void OnLoginClicked(object sender, EventArgs e)
        {
            await DisplayAlert("Info", "Login todavía no implementado.", "OK");
        }
    }
}