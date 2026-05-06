using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustMeetinPoint.Maui.Features.Groups.Models;
using JustMeetinPoint.Maui.Features.Groups.Services;
using System.Collections.ObjectModel;
using System.Net.Sockets;

namespace JustMeetinPoint.Maui.Features.Groups.ViewModels;

public partial class CreateGroupViewModel : ObservableObject
{
    private const int MinGroupNameLength = 3;
    private const int MaxGroupNameLength = 50;
    private const int MaxDescriptionLength = 250;

    private readonly IGroupService _groupService;

    public CreateGroupViewModel(IGroupService groupService)
    {
        _groupService = groupService;

        Categories = new ObservableCollection<CategoryOptionModel>
        {
            new() { Name = "Café" },
            new() { Name = "Comida" },
            new() { Name = "Ocio" },
            new() { Name = "Trabajo" }
        };

        Methods = new ObservableCollection<MethodOptionModel>
        {
            new()
            {
                Name = "Centroide",
                Description = "Método inicial disponible actualmente.",
                Value = "centroid",
                IsSelected = true,
                IsEnabled = true
            },
            new()
            {
                Name = "Óptimo",
                Description = "Próximamente.",
                Value = "optimal",
                IsSelected = false,
                IsEnabled = false
            },
            new()
            {
                Name = "Por recomendación",
                Description = "Próximamente.",
                Value = "recommended",
                IsSelected = false,
                IsEnabled = false
            }
        };

        SelectedMethod = "centroid";
    }

    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private string description = string.Empty;

    [ObservableProperty]
    private string selectedMethod = "centroid";

    [ObservableProperty]
    private string selectedCategory = string.Empty;

    [ObservableProperty]
    private string errorMessage = string.Empty;

    [ObservableProperty]
    private bool isBusy;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool IsNotBusy => !IsBusy;

    public ObservableCollection<CategoryOptionModel> Categories { get; }

    public ObservableCollection<MethodOptionModel> Methods { get; }

    partial void OnNameChanged(string value)
    {
        ClearError();
    }

    partial void OnDescriptionChanged(string value)
    {
        ClearError();
    }

    partial void OnSelectedCategoryChanged(string value)
    {
        ClearError();
    }

    partial void OnSelectedMethodChanged(string value)
    {
        ClearError();
    }

    partial void OnErrorMessageChanged(string value)
    {
        OnPropertyChanged(nameof(HasError));
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNotBusy));
    }

    [RelayCommand]
    private void SelectCategory(CategoryOptionModel option)
    {
        if (option is null || IsBusy)
            return;

        foreach (var item in Categories)
            item.IsSelected = false;

        option.IsSelected = true;
        SelectedCategory = option.Name;
    }

    [RelayCommand]
    private void SelectMethod(MethodOptionModel option)
    {
        if (option is null || !option.IsEnabled || IsBusy)
            return;

        foreach (var item in Methods)
            item.IsSelected = false;

        option.IsSelected = true;
        SelectedMethod = option.Value;
    }

    [RelayCommand]
    private async Task CreateGroupAsync()
    {
        if (IsBusy)
            return;

        ClearError();

        string normalizedName = Name?.Trim() ?? string.Empty;
        string normalizedDescription = Description?.Trim() ?? string.Empty;
        string normalizedCategory = SelectedCategory?.Trim() ?? string.Empty;
        string normalizedMethod = SelectedMethod?.Trim() ?? string.Empty;

        if (!ValidateInput(
                normalizedName,
                normalizedDescription,
                normalizedCategory,
                normalizedMethod))
        {
            return;
        }

        try
        {
            IsBusy = true;

            var result = await _groupService.CreateGroupAsync(
                normalizedName,
                normalizedDescription,
                normalizedMethod,
                normalizedCategory);

            if (result is null || string.IsNullOrWhiteSpace(result.GroupCode))
            {
                SetError("No se pudo crear el grupo.");
                return;
            }

            await Shell.Current.GoToAsync(
                $"group-lobby?groupCode={Uri.EscapeDataString(result.GroupCode)}&isCurrentUserHost=true");
        }
        catch (ArgumentException ex)
        {
            SetError(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            SetError(ex.Message);
        }
        catch (SocketException)
        {
            SetError("No se pudo conectar con el servidor.");
        }
        catch (Exception)
        {
            SetError("Error al crear el grupo. Inténtalo de nuevo.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool ValidateInput(
        string normalizedName,
        string normalizedDescription,
        string normalizedCategory,
        string normalizedMethod)
    {
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            SetError("Introduce un nombre.");
            return false;
        }

        if (normalizedName.Length < MinGroupNameLength)
        {
            SetError($"El nombre debe tener al menos {MinGroupNameLength} caracteres.");
            return false;
        }

        if (normalizedName.Length > MaxGroupNameLength)
        {
            SetError($"El nombre no puede superar {MaxGroupNameLength} caracteres.");
            return false;
        }

        if (normalizedDescription.Length > MaxDescriptionLength)
        {
            SetError($"La descripción no puede superar {MaxDescriptionLength} caracteres.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(normalizedCategory))
        {
            SetError("Selecciona un motivo.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(normalizedMethod))
        {
            SetError("Selecciona un método de cálculo.");
            return false;
        }

        return true;
    }

    private void SetError(string message)
    {
        ErrorMessage = message;
    }

    private void ClearError()
    {
        if (!string.IsNullOrWhiteSpace(ErrorMessage))
            ErrorMessage = string.Empty;
    }
}