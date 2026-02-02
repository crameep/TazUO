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

        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1, 0.2f, 0.2f, 1));
        ImGui.Text(_errorMsg);
        ClipboardOnClick(_errorMsg);
        ImGui.PopStyleColor();
        ImGuiComponents.Tooltip("Click this error to copy it to your clipboard.");

        ImGui.Text($"Line Content:");
        Vector2 size = ImGui.GetContentRegionAvail();
        size.Y = 100;
        ImGui.InputTextMultiline("###ErrorLineContent", ref _lineContent, uint.MaxValue, size);

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
