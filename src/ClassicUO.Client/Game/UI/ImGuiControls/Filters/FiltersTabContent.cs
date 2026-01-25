using ImGuiNET;

namespace ClassicUO.Game.UI.ImGuiControls
{
    public class FiltersTabContent : TabContent
    {
        private GraphicReplacementTabContent _graphicsTab;
        private JournalFilterTabContent _journalFilterTab;
        private SoundFilterTabContent _soundFilterTab;
        private SeasonFilterTabContent _seasonFilterTab;

        public FiltersTabContent()
        {
            _graphicsTab = new GraphicReplacementTabContent();
            _journalFilterTab = new JournalFilterTabContent();
            _soundFilterTab = new SoundFilterTabContent();
            _seasonFilterTab = new SeasonFilterTabContent();
        }

        public override void DrawContent()
        {
            ImGui.Spacing();

            if (ImGui.BeginTabBar("##FilterTabs", ImGuiTabBarFlags.None))
            {
                if (ImGui.BeginTabItem("Graphics"))
                {
                    _graphicsTab.DrawContent();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Journal Filter"))
                {
                    _journalFilterTab.DrawContent();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Sound Filter"))
                {
                    _soundFilterTab.DrawContent();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Season Filter"))
                {
                    _seasonFilterTab.DrawContent();
                    ImGui.EndTabItem();
                }

                ImGui.EndTabBar();
            }
        }

        public override void Dispose()
        {
            _graphicsTab?.Dispose();
            _journalFilterTab?.Dispose();
            _soundFilterTab?.Dispose();
            _seasonFilterTab?.Dispose();
            base.Dispose();
        }
    }
}
