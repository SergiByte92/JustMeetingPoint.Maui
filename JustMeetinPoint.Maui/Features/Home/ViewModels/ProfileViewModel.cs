namespace JustMeetinPoint.Maui.Features.Home.ViewModels;

public class ProfileViewModel
{
    public string UserInitials { get; set; } = "SG";
    public string FullName { get; set; } = "Sergi Garcia";
    public string Email { get; set; } = "sergi@email.com";

    public string MeetingsCreated { get; set; } = "12";
    public string CompletedGroups { get; set; } = "8";
    public string AvgTravelTimeText { get; set; } = "27 min";
    public string LastActivityText { get; set; } = "Hoy";
}