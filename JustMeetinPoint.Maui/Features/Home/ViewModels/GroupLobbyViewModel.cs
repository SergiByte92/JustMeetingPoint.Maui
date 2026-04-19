using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustMeetinPoint.Maui.Features.Home.Services;

namespace JustMeetinPoint.Maui.Features.Home.ViewModels;

[QueryProperty(nameof(GroupCode), "groupCode")]
[QueryProperty(nameof(IsCurrentUserHostRaw), "isCurrentUserHost")]
public partial class GroupLobbyViewModel : ObservableObject
{
    private readonly IGroupService _groupService;

    public GroupLobbyViewModel(IGroupService groupService)
    {
        _groupService = groupService;
    }

    // [ObservableProperty] genera automáticamente:
    //   - La propiedad pública con nombre en PascalCase (groupCode → GroupCode)
    //   - El campo privado backing
    //   - La llamada a OnPropertyChanged en el setter
    //   - Un método partial OnGroupCodeChanged() que puedes implementar

    [ObservableProperty] private string groupCode = string.Empty;
    [ObservableProperty] private int memberCount;
    [ObservableProperty] private bool hasStarted;
    [ObservableProperty] private bool isCurrentUserHost;
    [ObservableProperty] private bool isBusy;

    // ✅ AÑADIDO: propiedad para mostrar errores en la UI.
    // Antes el catch estaba vacío — el usuario no sabía qué había fallado.
    [ObservableProperty] private string errorMessage = string.Empty;

    // QueryProperty sólo puede recibir strings desde la URL de navegación.
    // Por eso IsCurrentUserHost (bool) necesita esta propiedad puente que
    // parsea el string "True"/"False" manualmente.
    public string IsCurrentUserHostRaw
    {
        set
        {
            if (bool.TryParse(value, out bool parsed))
                IsCurrentUserHost = parsed;
        }
    }

    // Propiedades computadas: se derivan de otras propiedades observables.
    // No almacenan valor propio — se recalculan cada vez que se leen.
    // OnMemberCountChanged y OnHasStartedChanged notifican manualmente
    // que estas propiedades derivadas también han cambiado.
    public string StatusText => HasStarted
        ? "El grupo ya ha iniciado."
        : "Esperando a más participantes...";

    public string ParticipantsText => $"{MemberCount} participante{(MemberCount == 1 ? "" : "s")} conectado{(MemberCount == 1 ? "" : "s")}";

    // Estos métodos los genera [ObservableProperty] como partial vacíos.
    // Al implementarlos, se ejecutan justo después de que la propiedad cambia.
    partial void OnGroupCodeChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            // BeginInvokeOnMainThread: necesario porque OnGroupCodeChanged
            // puede invocarse desde un hilo de background (QueryProperty).
            // La navegación y las llamadas async a la UI deben hacerse en el hilo principal.
            MainThread.BeginInvokeOnMainThread(async () => await LoadLobbyAsync());
    }

    partial void OnMemberCountChanged(int value) => OnPropertyChanged(nameof(ParticipantsText));
    partial void OnHasStartedChanged(bool value) => OnPropertyChanged(nameof(StatusText));

    [RelayCommand]
    private async Task LoadLobbyAsync()
    {
        if (IsBusy || string.IsNullOrWhiteSpace(GroupCode)) return;

        try
        {
            IsBusy = true;
            ErrorMessage = string.Empty; // ✅ Limpia el error anterior

            var lobby = await _groupService.RefreshLobbyAsync(GroupCode, IsCurrentUserHost);

            MemberCount = lobby.MemberCount;
            HasStarted = lobby.HasStarted;
        }
        catch (Exception ex)
        {
            // ✅ CORREGIDO: antes era catch{} vacío.
            // Ahora el usuario ve qué falló y el desarrollador puede depurar.
            ErrorMessage = $"Error al cargar el lobby: {ex.Message}";
            Console.WriteLine($"[GroupLobbyViewModel] Error: {ex}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RefreshAsync() => await LoadLobbyAsync();

    [RelayCommand]
    private async Task LeaveGroupAsync()
    {
        if (IsBusy || string.IsNullOrWhiteSpace(GroupCode)) return;

        try
        {
            IsBusy = true;
            await _groupService.LeaveGroupAsync(GroupCode);
            await Shell.Current.GoToAsync("//main/groups");
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error al salir del grupo: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}