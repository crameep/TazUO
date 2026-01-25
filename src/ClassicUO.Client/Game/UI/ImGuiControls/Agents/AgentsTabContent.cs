using ImGuiNET;
using ClassicUO.Configuration;

namespace ClassicUO.Game.UI.ImGuiControls
{
    public class AgentsTabContent : TabContent
    {
        private readonly Profile _profile = ProfileManager.CurrentProfile;

        private AutoLootTabContent _autoLootTab;
        private DressAgentTabContent _dressAgentTab;
        private AutoBuyTabContent _autoBuyTab;
        private AutoSellTabContent _autoSellTab;
        private BandageAgentTabContent _bandageAgentTab;

        public AgentsTabContent()
        {
            _autoLootTab = new AutoLootTabContent();
            _dressAgentTab = new DressAgentTabContent();
            _autoBuyTab = new AutoBuyTabContent();
            _autoSellTab = new AutoSellTabContent();
            _bandageAgentTab = new BandageAgentTabContent();
        }

        public override void DrawContent()
        {
            if (_profile == null)
            {
                ImGui.Text("Profile not loaded");
                return;
            }

            ImGui.Spacing();

            if (ImGui.BeginTabBar("##Agents Tabs", ImGuiTabBarFlags.None))
            {
                if (ImGui.BeginTabItem("Auto Loot"))
                {
                    _autoLootTab.DrawContent();
                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem("Dress Agent"))
                {
                    _dressAgentTab.DrawContent();
                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem("Auto Buy"))
                {
                    _autoBuyTab.DrawContent();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Auto Sell"))
                {
                    _autoSellTab.DrawContent();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Bandage"))
                {
                    _bandageAgentTab.DrawContent();
                    ImGui.EndTabItem();
                }

                ImGui.EndTabBar();
            }
        }

        public override void Dispose()
        {
            _autoLootTab?.Dispose();
            _dressAgentTab?.Dispose();
            _autoBuyTab?.Dispose();
            _autoSellTab?.Dispose();
            _bandageAgentTab?.Dispose();
            base.Dispose();
        }
    }
}
