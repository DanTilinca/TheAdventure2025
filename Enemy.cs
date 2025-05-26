using TheAdventure.Models;

namespace TheAdventure
{
    public class Enemy : RenderableGameObject
    {
        private const float DefaultSpeed = 128f;
        private const float PatrolSpeed = 64f;
        private const int DetectionRadius = 200;
        private const int DamageDistance = 16;

        private readonly float _speed;
        private (float X, float Y) _direction;
        private DateTimeOffset _nextDirectionChange;
        private bool _chasing;

        public bool ShouldBeRemoved { get; private set; } = false;

        public Enemy(SpriteSheet spriteSheet, (int X, int Y) startPosition, float speed = DefaultSpeed)
            : base(spriteSheet, startPosition)
        {
            _speed = speed;
            _nextDirectionChange = DateTimeOffset.Now;
            PickRandomDirection();
            SpriteSheet.ActivateAnimation("Idle");
        }

        private void PickRandomDirection()
        {
            var angle = Random.Shared.NextDouble() * MathF.PI * 2;
            _direction = ((float)Math.Cos(angle), (float)Math.Sin(angle));
            _nextDirectionChange = DateTimeOffset.Now.AddSeconds(Random.Shared.Next(1, 4));
        }

        public void Update(Engine engine, double deltaTimeMs)
        {
            var (px, py) = engine.GetPlayerPosition();
            var dx = px - Position.X;
            var dy = py - Position.Y;
            var distance = MathF.Sqrt(dx * dx + dy * dy);

            if (distance < DamageDistance)
            {
                engine.DamagePlayerFrom(Position);
                ShouldBeRemoved = true;
                return;
            }

            if (distance < DetectionRadius)
            {
                _chasing = true;
            }
            else if (distance > DetectionRadius * 1.5)
            {
                _chasing = false;
            }

            var moveDistance = (_chasing ? _speed : PatrolSpeed) * (float)(deltaTimeMs / 1000.0f);

            float moveX, moveY;

            if (_chasing)
            {
                var nx = dx / distance;
                var ny = dy / distance;
                moveX = nx;
                moveY = ny;
            }
            else
            {
                if (DateTimeOffset.Now > _nextDirectionChange)
                {
                    PickRandomDirection();
                }
                moveX = _direction.X;
                moveY = _direction.Y;
            }

            if (_chasing || !_chasing && (moveX != 0 || moveY != 0))
            {
                if (SpriteSheet.ActiveAnimation != SpriteSheet.Animations["Walk"])
                    SpriteSheet.ActivateAnimation("Walk");
            }
            else
            {
                if (SpriteSheet.ActiveAnimation != SpriteSheet.Animations["Idle"])
                    SpriteSheet.ActivateAnimation("Idle");
            }

            Position = (
                Position.X + (int)(moveX * moveDistance),
                Position.Y + (int)(moveY * moveDistance)
            );
        }
    }
}