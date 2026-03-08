// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Configuration;
using ClassicUO.Game.UI.Controls;

namespace ClassicUO.Game.UI.Gumps.Login
{
    public class MobileConnectionSettingsGump : Gump
    {
        private readonly StbTextBox _serverNameTextBox;
        private readonly StbTextBox _serverIpTextBox;
        private readonly StbTextBox _serverPortTextBox;
        private readonly StbTextBox _updateUrlTextBox;
        private readonly StbTextBox _updateHostTextBox;
        private readonly StbTextBox _updatePortTextBox;
        private readonly StbTextBox _updatePublicKeyTextBox;

        public MobileConnectionSettingsGump(World world) : base(world, 0, 0)
        {
            CanCloseWithRightClick = true;
            CanMove = true;
            IsModal = true;

            X = 90;
            Y = 90;

            Add
            (
                new ResizePic(0x13BE)
                {
                    X = 0,
                    Y = 0,
                    Width = 470,
                    Height = 380
                }
            );

            Add(new Label("Mobile Connection Settings", false, 0x0386, font: 2) { X = 20, Y = 16 });

            _serverNameTextBox = AddField("Server Name", 54, Settings.GlobalSettings.ServerName, 430, false);
            _serverIpTextBox = AddField("Server IP", 94, Settings.GlobalSettings.IP, 430, false);
            _serverPortTextBox = AddField("Server Port", 134, Settings.GlobalSettings.Port.ToString(), 120, true);
            _updateUrlTextBox = AddField("Update URL", 174, Settings.GlobalSettings.UpdateUrl, 430, false);
            _updateHostTextBox = AddField("Update Host", 214, Settings.GlobalSettings.UpdateHost, 430, false);
            _updatePortTextBox = AddField("Update Port", 254, Settings.GlobalSettings.UpdatePort.ToString(), 120, true);
            _updatePublicKeyTextBox = AddField("Update Public Key", 294, Settings.GlobalSettings.UpdatePublicKey, 430, false);

            var cancel = new NiceButton(250, 340, 90, 24, ButtonAction.Activate, "Cancel")
            {
                IsSelectable = false,
                ButtonParameter = (int)Buttons.Cancel
            };
            Add(cancel);

            var save = new NiceButton(350, 340, 90, 24, ButtonAction.Activate, "Save")
            {
                IsSelectable = false,
                ButtonParameter = (int)Buttons.Save
            };
            Add(save);

            _serverNameTextBox.SetKeyboardFocus();
        }

        public override void OnButtonClick(int buttonID)
        {
            switch ((Buttons) buttonID)
            {
                case Buttons.Save:
                    if (!ushort.TryParse(_serverPortTextBox.Text, out ushort serverPort) || serverPort == 0)
                    {
                        serverPort = 2593;
                    }

                    if (!ushort.TryParse(_updatePortTextBox.Text, out ushort updatePort) || updatePort == 0)
                    {
                        updatePort = 443;
                    }

                    Settings.GlobalSettings.ServerName = _serverNameTextBox.Text;
                    Settings.GlobalSettings.IP = _serverIpTextBox.Text;
                    Settings.GlobalSettings.Port = serverPort;
                    Settings.GlobalSettings.UpdateUrl = _updateUrlTextBox.Text;
                    Settings.GlobalSettings.UpdateHost = _updateHostTextBox.Text;
                    Settings.GlobalSettings.UpdatePort = updatePort;
                    Settings.GlobalSettings.UpdatePublicKey = _updatePublicKeyTextBox.Text;
                    Settings.GlobalSettings.NormalizeAndValidate();
                    Settings.GlobalSettings.Save();
                    Dispose();

                    break;

                case Buttons.Cancel:
                    Dispose();

                    break;
            }
        }

        private StbTextBox AddField(string label, int y, string value, int width, bool numbersOnly)
        {
            Add(new Label(label, false, 0x0386, font: 1) { X = 20, Y = y + 3 });

            Add
            (
                new ResizePic(0x0BB8)
                {
                    X = 140,
                    Y = y,
                    Width = width,
                    Height = 28
                }
            );

            var textBox = new StbTextBox(5, 256, width - 16, false, hue: 0x034F)
            {
                X = 148,
                Y = y + 3,
                Width = width - 16,
                Height = 22,
                NumbersOnly = numbersOnly
            };

            textBox.SetText(value ?? string.Empty);
            Add(textBox);

            return textBox;
        }

        private enum Buttons
        {
            Save,
            Cancel
        }
    }
}
