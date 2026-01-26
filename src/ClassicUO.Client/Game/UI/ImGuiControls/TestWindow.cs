using ClassicUO.Utility;
using ImGuiNET;

namespace ClassicUO.Game.UI.ImGuiControls;

public class TestWindow : SingletonImGuiWindow<TestWindow>
{
    public static string TestMessage = string.Empty;

    private int _currency = 0;
    private string _formattedCurrency = string.Empty;
    private string _formattedAsInt = string.Empty;

    //This is intended for testing purposes, if you need a quick ui to debug something feel free to edit this as needed.
    public TestWindow() : base("Test Window")
    {
        WindowFlags = ImGuiWindowFlags.AlwaysAutoResize;
    }

    public override void DrawContent()
    {
        ImGui.Text(TestMessage);

        if (ImGui.InputInt("Currency Test", ref _currency))
        {
            _formattedCurrency = StringHelper.FormatAsCurrency(_currency);
            if (StringHelper.TryParseCurrency(_formattedCurrency, out int val))
            {
                _formattedAsInt = val.ToString();
            }
        }

        ImGui.Text(_formattedCurrency);
        ImGui.Text(_formattedAsInt);
    }
}
