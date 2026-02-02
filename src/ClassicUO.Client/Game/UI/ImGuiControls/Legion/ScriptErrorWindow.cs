using System.Numerics;
using ClassicUO.LegionScripting;
using ClassicUO.Utility;
using ImGuiNET;

namespace ClassicUO.Game.UI.ImGuiControls.Legion;

public class ScriptErrorWindow : ImGuiWindow
{
    private readonly ScriptErrorDetails _errorDetails;
    private static int id = 1;

    public ScriptErrorWindow(ScriptErrorDetails errorDetails) : base("Script Error " + id++)
    {
        _errorDetails = errorDetails;
        _errorMsg = errorDetails.ErrorMsg;
        _lineContent = errorDetails.LineContent;
        WindowFlags = ImGuiWindowFlags.NoResize;
    }

    private Vector2 _size = new(500, 300);
    private string _errorMsg, _lineContent;

    public override void DrawContent()
    {
        ImGui.SetWindowSize(_size);
        ImGui.Text("Your script encountered an error, here's what we know:");

        ImGui.Text($"Filename: {_errorDetails.Script.FileName}");

        ImGui.Text($"Line Number: {_errorDetails.LineNumber}");

        Vector2 size = ImGui.GetContentRegionAvail();
        size.Y = 100;
        ImGui.InputTextMultiline("###Error", ref _errorMsg, uint.MaxValue, size);

        ImGui.Text($"Line Content: {_errorDetails.LineContent}");
        ImGui.InputText("###ErrorLineContent", ref _lineContent, uint.MaxValue);

        if (ImGui.Button("Edit")) ImGuiManager.AddWindow(new ScriptEditorWindow(_errorDetails.Script));

        ImGui.SameLine();

        if (ImGui.Button("Edit Externally")) FileSystemHelper.OpenFileWithDefaultApp(_errorDetails.Script.FullPath);
    }
}

public struct ScriptErrorDetails(string errorMsg, int lineNumber, string lineContent, ScriptFile script)
{
    public string ErrorMsg { get; } = errorMsg;
    public int LineNumber { get; } = lineNumber;
    public string LineContent { get; } = lineContent;
    public ScriptFile Script { get; } = script;
}
