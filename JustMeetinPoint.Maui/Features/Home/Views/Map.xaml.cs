using JustMeetinPoint.Maui.Features.Home.ViewModels;

namespace JustMeetinPoint.Maui.Features.Home.Views;

public partial class MapView : ContentPage
{
    public MapView(MapViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}