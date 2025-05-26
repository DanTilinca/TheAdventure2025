using TheAdventure.Models;

namespace TheAdventure
{
    public class HealthPickup : RenderableGameObject
    {
        private readonly int _textureId;

        public HealthPickup(int textureId, (int X, int Y) position)
            : base(null!, position)
        {
            _textureId = textureId;
        }

        public override void Render(GameRenderer renderer)
        {
            var rect = new Silk.NET.Maths.Rectangle<int>(
                Position.X - 16, Position.Y - 16,
                32, 32
            );

            renderer.RenderTexture(_textureId, new Silk.NET.Maths.Rectangle<int>(0, 0, 32, 32), rect);
        }
    }
}
