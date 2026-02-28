using System;
using System.Globalization;
using System.Numerics;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using ImGuiNET;

namespace ClassicUO.Game.UI.ImGuiControls
{
    public class TomeTabContent : TabContent
    {
        private int _selectedIndex = -1;
        private TomeDefinition _selected;
        private bool _captureNextButton;
        private int _captureStartSequence;
        private string _gumpIdInput = string.Empty;
        private string _buttonIdInput = string.Empty;

        public override void DrawContent()
        {
            if (TomeManager.Instance == null)
            {
                ImGui.Text("Tome manager not loaded");
                return;
            }

            if (ImGui.BeginTable("TomeTable", 2, ImGuiTableFlags.Resizable))
            {
                ImGui.TableSetupColumn("Tomes", ImGuiTableColumnFlags.WidthFixed, 280);
                ImGui.TableSetupColumn("Details", ImGuiTableColumnFlags.WidthStretch);

                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                DrawList();

                ImGui.TableSetColumnIndex(1);
                DrawDetails();

                ImGui.EndTable();
            }
        }

        private void DrawList()
        {
            if (ImGui.Button("Add Tome"))
            {
                TomeDefinition tome = new TomeDefinition { Name = GetUniqueName("Tome") };
                TomeManager.Instance.TomeDefinitions.Add(tome);
                _selectedIndex = TomeManager.Instance.TomeDefinitions.Count - 1;
                _selected = tome;
                SyncInputsFromSelection();
            }

            ImGui.SeparatorText("Definitions");

            for (int i = 0; i < TomeManager.Instance.TomeDefinitions.Count; i++)
            {
                TomeDefinition tome = TomeManager.Instance.TomeDefinitions[i];
                bool selected = i == _selectedIndex;
                if (ImGui.Selectable($"{tome.Name}##tome{i}", selected))
                {
                    _selectedIndex = i;
                    _selected = tome;
                    SyncInputsFromSelection();
                }
            }
        }

        private void DrawDetails()
        {
            if (_selected == null)
            {
                ImGui.Text("Select a tome to edit");
                return;
            }

            TryApplyCapturedButton();

            string name = _selected.Name;
            ImGui.SetNextItemWidth(220);
            if (ImGui.InputText("Name", ref name, 100))
                _selected.Name = name;

            if (ImGui.Button("Set Tome"))
            {
                GameActions.Print("Select a [TOME] item", 82);
                World.Instance.TargetManager.SetTargeting(target =>
                {
                    if (target is not Entity entity || !SerialHelper.IsItem(entity))
                    {
                        GameActions.Print("Only items can be selected.", Constants.HUE_ERROR);
                        return;
                    }

                    _selected.TomeSerial = entity.Serial;
                    GameActions.Print($"Tome set to 0x{entity.Serial:X4} ({entity.Name})", Constants.HUE_SUCCESS);
                });
            }

            ImGui.SameLine();
            if (!_captureNextButton)
            {
                if (ImGui.Button("Capture Gump Button"))
                {
                    _captureNextButton = true;
                    _captureStartSequence = GumpButtonCapture.Sequence;
                    GameActions.Print("Capture armed. Click the tome button you want to record.", 82);
                }
            }
            else if (ImGui.Button("Cancel Capture"))
            {
                _captureNextButton = false;
                GameActions.Print("Tome capture canceled.");
            }

            ImGui.Text($"Tome serial: 0x{_selected.TomeSerial:X8}");

            ImGui.SetNextItemWidth(130);
            if (ImGui.InputText("Gump ID", ref _gumpIdInput, 20))
            {
                if (TryParseUInt(_gumpIdInput, out uint value))
                    _selected.GumpId = value;
            }

            ImGui.SetNextItemWidth(130);
            if (ImGui.InputText("Add Button", ref _buttonIdInput, 12))
            {
                if (int.TryParse(_buttonIdInput, out int value))
                    _selected.AddButtonId = value;
            }

            TomeMode mode = _selected.Mode;
            if (ImGui.BeginCombo("Mode", mode.ToString()))
            {
                foreach (TomeMode m in Enum.GetValues(typeof(TomeMode)))
                {
                    bool isSelected = mode == m;
                    if (ImGui.Selectable(m.ToString(), isSelected))
                        _selected.Mode = m;
                }

                ImGui.EndCombo();
            }

            if (_selected.Mode == TomeMode.TargetContainer)
            {
                bool selfTarget = _selected.TargetSerial == 0;
                if (ImGui.Checkbox("Self-Target", ref selfTarget))
                    _selected.TargetSerial = selfTarget ? 0 : _selected.TargetSerial;

                ImGui.SameLine();
                if (ImGui.Button("Set Target Container"))
                {
                    GameActions.Print("Select target container", 82);
                    World.Instance.TargetManager.SetTargeting(target =>
                    {
                        if (target is not Entity entity || !SerialHelper.IsItem(entity))
                        {
                            GameActions.Print("Only container items are valid.", Constants.HUE_ERROR);
                            return;
                        }

                        _selected.TargetSerial = entity.Serial;
                    });
                }

                if (_selected.TargetSerial != 0)
                    ImGui.Text($"Target: 0x{_selected.TargetSerial:X8}");
            }

            int delay = _selected.Delay;
            if (ImGui.InputInt("Delay (ms)", ref delay, 50, 100))
                _selected.Delay = Math.Max(0, delay);

            bool requiresWalk = _selected.RequiresWalk;
            if (ImGui.Checkbox("Requires Walk", ref requiresWalk))
                _selected.RequiresWalk = requiresWalk;

            if (ImGui.Button("Save"))
            {
                TomeManager.Instance.Save();
                GameActions.Print("Saved tome definitions.", Constants.HUE_SUCCESS);
            }

            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.8f, 0.2f, 0.2f, 1.0f));
            if (ImGui.Button("Delete"))
            {
                TomeManager.Instance.TomeDefinitions.Remove(_selected);
                _selected = null;
                _selectedIndex = -1;
            }

            ImGui.PopStyleColor();
        }

        private void SyncInputsFromSelection()
        {
            if (_selected == null)
                return;

            _gumpIdInput = _selected.GumpId == 0 ? string.Empty : $"0x{_selected.GumpId:X8}";
            _buttonIdInput = _selected.AddButtonId.ToString();
        }

        private void TryApplyCapturedButton()
        {
            if (!_captureNextButton || _selected == null)
                return;

            if (GumpButtonCapture.Sequence == _captureStartSequence)
                return;

            _selected.GumpId = GumpButtonCapture.LastGumpId;
            _selected.AddButtonId = GumpButtonCapture.LastButtonId;
            SyncInputsFromSelection();
            _captureNextButton = false;

            GameActions.Print($"Captured tome mapping: gump 0x{_selected.GumpId:X8}, button {_selected.AddButtonId}.", Constants.HUE_SUCCESS);
        }

        private string GetUniqueName(string baseName)
        {
            string name = baseName;
            int i = 2;
            while (TomeManager.Instance.TomeDefinitions.Exists(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)))
                name = $"{baseName} ({i++})";

            return name;
        }

        private static bool TryParseUInt(string value, out uint result)
        {
            result = 0;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string text = value.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return uint.TryParse(text.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result);

            return uint.TryParse(text, out result);
        }
    }
}
