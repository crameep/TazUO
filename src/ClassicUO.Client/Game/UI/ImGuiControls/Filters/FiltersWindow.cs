using ImGuiNET;

namespace ClassicUO.Game.UI.ImGuiControls
{
    public class FiltersWindow : SingletonImGuiWindow<FiltersWindow>
    {
        private bool graphics = false;
        private bool journalFilters = false;
        private bool soundFilters = false;
        private FiltersWindow() : base("Filters Tab")
        {
            WindowFlags = ImGuiWindowFlags.AlwaysAutoResize;
        }

        public override void DrawContent()
        {
            ImGui.Spacing();

            if (ImGui.BeginTabBar("##FilterTabs", ImGuiTabBarFlags.None))
            {
                if (ImGui.BeginTabItem("Graphics"))
                {
                    GraphicReplacementWindow.GetInstance()?.DrawContent();
                    graphics = true;
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Journal Filter"))
                {
                    JournalFilterWindow.GetInstance()?.DrawContent();
                    journalFilters = true;
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Sound Filter"))
                {
                    SoundFilterWindow.GetInstance()?.DrawContent();
                    soundFilters = true;
                    ImGui.EndTabItem();
                }

                ImGui.EndTabBar();
            }
        }

        public override void Dispose()
        {
            base.Dispose();

            if(graphics)
                GraphicReplacementWindow.GetInstance()?.Dispose();

            if(journalFilters)
                JournalFilterWindow.GetInstance()?.Dispose();

            if(soundFilters)
                SoundFilterWindow.GetInstance()?.Dispose();
        }
    }
}
