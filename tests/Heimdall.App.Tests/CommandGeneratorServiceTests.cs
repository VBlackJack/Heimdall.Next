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

using TwinShell.Core.Constants;
using TwinShell.Core.Models;
using TwinShell.Core.Services;
using ActionModel = TwinShell.Core.Models.Action;

namespace Heimdall.App.Tests;

public sealed class CommandGeneratorServiceTests
{
    [Fact]
    public void GenerateCommand_NoParameters_PatternWithinLimit_ReturnsPattern()
    {
        CommandGeneratorService service = CreateService();
        CommandTemplate template = CreateTemplate("short", "Short", "uptime");
        Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.Ordinal);

        string command = service.GenerateCommand(template, values);

        Assert.Equal("uptime", command);
    }

    [Fact]
    public void GenerateCommand_NoParameters_PatternExceedsMaxLength_Throws()
    {
        CommandGeneratorService service = CreateService();
        CommandTemplate template = CreateTemplate("long-pattern", "Long pattern", new string('a', 1100));
        Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.Ordinal);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => service.GenerateCommand(template, values));

        Assert.Contains(ValidationConstants.MaxCommandLength.ToString(), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GenerateCommand_GeneratedResultExceedsMaxLength_Throws()
    {
        CommandGeneratorService service = CreateService();
        TemplateParameter parameter = CommandLibraryTestHelpers.RequiredParameter("val", "Value");
        string pattern = new string('a', 800) + "{val}" + new string('b', 100);
        CommandTemplate template = CreateTemplate("expanded-command", "Expanded command", pattern, parameter);
        Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["val"] = new string('c', ValidationConstants.MaxParameterLength)
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => service.GenerateCommand(template, values));

        Assert.Contains(ValidationConstants.MaxCommandLength.ToString(), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GenerateCommand_TooManyParameters_Throws()
    {
        CommandGeneratorService service = CreateService();
        TemplateParameter[] parameters = CreateParameters(ValidationConstants.MaxParametersPerTemplate + 1);
        CommandTemplate template = CreateTemplate("too-many", "Too many", "echo ok", parameters);
        Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.Ordinal);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => service.GenerateCommand(template, values));

        Assert.Contains(ValidationConstants.MaxParametersPerTemplate.ToString(), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GenerateCommand_ParameterCountAtLimit_Succeeds()
    {
        CommandGeneratorService service = CreateService();
        TemplateParameter[] parameters = CreateParameters(ValidationConstants.MaxParametersPerTemplate);
        CommandTemplate template = CreateTemplate("at-limit", "At limit", "echo ok", parameters);
        Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.Ordinal);

        string command = service.GenerateCommand(template, values);

        Assert.Equal("echo ok", command);
    }

    [Fact]
    public void ValidateParameters_TooManyParameters_ReturnsFalseWithCountError()
    {
        CommandGeneratorService service = CreateService();
        TemplateParameter[] parameters = CreateParameters(ValidationConstants.MaxParametersPerTemplate + 1);
        CommandTemplate template = CreateTemplate("too-many-validation", "Too many validation", "echo ok", parameters);
        Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.Ordinal);

        bool isValid = service.ValidateParameters(template, values, out List<string> errors);

        Assert.False(isValid);
        Assert.Contains(errors, error => error.Contains(ValidationConstants.MaxParametersPerTemplate.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateParameters_WithinBounds_ReturnsTrue()
    {
        CommandGeneratorService service = CreateService();
        TemplateParameter parameter = CommandLibraryTestHelpers.RequiredParameter("name", "Name");
        CommandTemplate template = CreateTemplate("valid", "Valid", "echo {name}", parameter);
        Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["name"] = "alpha"
        };

        bool isValid = service.ValidateParameters(template, values, out List<string> errors);

        Assert.True(isValid);
        Assert.Empty(errors);
    }

    private static CommandGeneratorService CreateService() =>
        new CommandGeneratorService(new FakeTwinShellLocalizationService());

    private static CommandTemplate CreateTemplate(
        string id,
        string title,
        string pattern,
        params TemplateParameter[] parameters)
    {
        ActionModel action = CommandLibraryTestHelpers.CreateLinuxAction(id, title, pattern, parameters);
        return action.LinuxCommandTemplate!;
    }

    private static TemplateParameter[] CreateParameters(int count)
    {
        TemplateParameter[] parameters = new TemplateParameter[count];
        for (int index = 0; index < parameters.Length; index++)
        {
            parameters[index] = CommandLibraryTestHelpers.OptionalParameter($"p{index}", $"Parameter {index}");
        }

        return parameters;
    }
}
