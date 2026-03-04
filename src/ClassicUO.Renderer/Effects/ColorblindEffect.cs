using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ClassicUO.Renderer
{
    public class ColorblindEffect : Effect
    {
        public ColorblindEffect(GraphicsDevice graphicsDevice)
            : base(graphicsDevice, Resources.GetColorblindShader().ToArray())
        {
            MatrixTransform = Parameters["MatrixTransform"];
            Row0 = Parameters["Row0"];
            Row1 = Parameters["Row1"];
            Row2 = Parameters["Row2"];
        }

        public EffectParameter MatrixTransform { get; }
        public EffectParameter Row0 { get; }
        public EffectParameter Row1 { get; }
        public EffectParameter Row2 { get; }
    }
}
