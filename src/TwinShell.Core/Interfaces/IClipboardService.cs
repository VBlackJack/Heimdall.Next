namespace TwinShell.Core.Interfaces;

/// <summary>
/// Service for clipboard operations
/// </summary>
public interface IClipboardService
{
    void SetText(string text);
    string GetText();
}
