using JustMeetinPoint.Maui.Features.Dashboard.ViewModels;

namespace JustMeetinPoint.Maui.Features.Dashboard.Views;

public partial class HomeView : ContentPage
{
    private readonly HomeViewModel _viewModel;

    public HomeView(HomeViewModel viewModel)
    {
        InitializeComponent();

        BindingContext = viewModel;
        _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        await _viewModel.LoadAsync();
    }
}