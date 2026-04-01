using TwinShell.Core.Models;

namespace TwinShell.Core.Interfaces;

/// <summary>
/// Service for generating commands from templates
/// </summary>
public interface ICommandGeneratorService
{
    string GenerateCommand(CommandTemplate template, Dictionary<string, string> parameterValues);
    Dictionary<string, string> GetDefaultParameterValues(CommandTemplate template);
    bool ValidateParameters(CommandTemplate template, Dictionary<string, string> parameterValues, out List<string> errors);
}
