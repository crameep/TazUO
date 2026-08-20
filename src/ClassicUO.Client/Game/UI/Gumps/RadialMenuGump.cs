using ClassicUO.Assets;
using ClassicUO.Game.Managers;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Input;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;

namespace ClassicUO.Game.UI.Gumps
{
    /// <summary>
    /// Ring of macro slots shown while a controller button is held, aimed with the stick.
    /// </summary>
    /// <remarks>
    /// Held rather than toggled, and released to fire, so a command costs one press instead of the
    /// three a toggled menu would need. Draws at the screen centre because the stick that aims it is
    /// also the one that moves the cursor, so anchoring to the cursor would drag the menu around.
    /// </remarks>
    internal sealed class RadialMenuGump : Gump
    {
        private const int RADIUS = 110;
        private const int LABEL_WIDTH = 130;

        private readonly Label[] _labels = new Label[RadialMenuManager.SLOT_COUNT];
        private readonly AlphaBlendControl _background;

        private int _selected = RadialMenuSelection.NO_SELECTION;

        public RadialMenuGump(World world) : base(world, 0, 0)
        {
            CanMove = false;
            AcceptMouseInput = false;
            CanCloseWithRightClick = false;
            IsModal = false;

            int size = (RADIUS * 2) + LABEL_WIDTH;

            Width = size;
            Height = size;

            _background = new AlphaBlendControl(0.65f)
            {
                Width = size,
                Height = size,
                BaseColor = Color.Black
            };

            Add(_background);

            int centre = size / 2;

            for (int slot = 0; slot < RadialMenuManager.SLOT_COUNT; slot++)
            {
                string name = RadialMenuManager.GetSlot(slot);

                var label = new Label(
                    string.IsNullOrEmpty(name) ? "-" : name,
                    true,
                    HUE_INACTIVE,
                    LABEL_WIDTH,
                    font: 0xFF,
                    align: TEXT_ALIGN_TYPE.TS_CENTER);

                Vector2 offset = RadialMenuSelection.SlotOffset(slot, RadialMenuManager.SLOT_COUNT, RADIUS);

                label.X = centre + (int)offset.X - (LABEL_WIDTH / 2);
                label.Y = centre + (int)offset.Y - (label.Height / 2);

                _labels[slot] = label;

                Add(label);
            }

            CentreOnScreen();
        }

        private const ushort HUE_INACTIVE = 0x0386;
        private const ushort HUE_SELECTED = 0x0035;

        /// <summary>Slot currently aimed at, or <see cref="RadialMenuSelection.NO_SELECTION"/>.</summary>
        public int Selected => _selected;

        /// <summary>Points the menu at <paramref name="direction"/>, highlighting the aimed slot.</summary>
        public void Aim(Vector2 direction)
        {
            int slot = RadialMenuSelection.SlotFromDirection(direction, RadialMenuManager.SLOT_COUNT);

            if (slot == _selected)
            {
                return;
            }

            _selected = slot;

            for (int i = 0; i < _labels.Length; i++)
            {
                _labels[i].Hue = i == slot ? HUE_SELECTED : HUE_INACTIVE;
            }
        }

        private void CentreOnScreen()
        {
            X = (Client.Game.Scene?.Camera.Bounds.Width ?? Width) / 2 - (Width / 2);
            Y = (Client.Game.Scene?.Camera.Bounds.Height ?? Height) / 2 - (Height / 2);
        }
    }
}
