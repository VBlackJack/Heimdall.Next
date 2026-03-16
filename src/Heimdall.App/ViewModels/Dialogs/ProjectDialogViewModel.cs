/*
 * Copyright 2026 Julien Bombled
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System.ComponentModel.DataAnnotations;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Heimdall.Core.Configuration;

namespace Heimdall.App.ViewModels.Dialogs;

/// <summary>
/// ViewModel for the project add/edit dialog.
/// Projects group servers with shared defaults and visual identifiers.
/// </summary>
public partial class ProjectDialogViewModel : ObservableValidator
{
    /// <summary>
    /// Predefined color palette for project badges.
    /// </summary>
    public static readonly string[] AvailableColors =
    [
        "#3B82F6", "#22C55E", "#EF4444", "#F59E0B",
        "#8B5CF6", "#EC4899", "#06B6D4", "#F97316"
    ];

    // --- Dialog state ---

    [ObservableProperty]
    private string _dialogTitle = "";

    [ObservableProperty]
    private bool _isEditMode;

    // --- Project fields ---

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "Project name is required.")]
    [MinLength(1, ErrorMessage = "Project name cannot be empty.")]
    [MaxLength(50, ErrorMessage = "Project name must not exceed 50 characters.")]
    private string _name = "";

    [ObservableProperty]
    [MaxLength(200, ErrorMessage = "Description must not exceed 200 characters.")]
    private string _description = "";

    [ObservableProperty]
    private string _color = "#3B82F6";

    [ObservableProperty]
    private string _icon = "";

    [ObservableProperty]
    private string _defaultSshUsername = "";

    [ObservableProperty]
    private string _defaultSshKeyPath = "";

    [ObservableProperty]
    private string _defaultGatewayId = "";

    // --- Validation ---

    [ObservableProperty]
    private string? _validationError;

    /// <summary>
    /// Triggers full validation of all annotated properties.
    /// </summary>
    [RelayCommand]
    private void Validate()
    {
        ValidateAllProperties();
        ValidationError = HasErrors ? GetFirstError() : null;
    }

    /// <summary>
    /// Maps the current ViewModel state to a flat DTO for persistence.
    /// </summary>
    public ProjectDto ToDto()
    {
        return new ProjectDto
        {
            Name = Name,
            Description = string.IsNullOrWhiteSpace(Description) ? null : Description,
            Color = Color
        };
    }

    /// <summary>
    /// Creates a ViewModel pre-populated from an existing DTO (for edit mode).
    /// </summary>
    /// <param name="dto">The project DTO to load values from.</param>
    /// <returns>A populated ProjectDialogViewModel in edit mode.</returns>
    public static ProjectDialogViewModel FromDto(ProjectDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new ProjectDialogViewModel
        {
            IsEditMode = true,
            Name = dto.Name,
            Description = dto.Description ?? "",
            Color = dto.Color ?? "#3B82F6"
        };
    }

    private string? GetFirstError()
    {
        var firstProperty = GetErrors()
            .OfType<System.ComponentModel.DataAnnotations.ValidationResult>()
            .FirstOrDefault();

        return firstProperty?.ErrorMessage;
    }
}

/// <summary>
/// Immutable result returned by the project dialog on close.
/// </summary>
/// <param name="Project">The project DTO with user-entered values.</param>
/// <param name="Saved">True if the user clicked Save, false if cancelled.</param>
public record ProjectDialogResult(ProjectDto Project, bool Saved);
