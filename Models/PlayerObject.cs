using Silk.NET.Maths;

namespace TheAdventure.Models;

public class PlayerObject : RenderableGameObject
{
    private const int _speed = 128; // pixels per second

    public int Health { get; private set; } = 3;

    public void LoseHealth()
    {
        if (State.State == PlayerState.GameOver)
            return;

        Health--;

        if (Health <= 0)
        {
            GameOver();
        }
    }


    public enum PlayerStateDirection
    {
        None = 0,
        Down,
        Up,
        Left,
        Right,
    }

    public enum PlayerState
    {
        None = 0,
        Idle,
        Move,
        Attack,
        GameOver
    }

    public (PlayerState State, PlayerStateDirection Direction) State { get; private set; }

    public PlayerObject(SpriteSheet spriteSheet, int x, int y) : base(spriteSheet, (x, y))
    {
        SetState(PlayerState.Idle, PlayerStateDirection.Down);
    }

    public void SetState(PlayerState state)
    {
        SetState(state, State.Direction);
    }

    public void SetState(PlayerState state, PlayerStateDirection direction)
    {
        if (State.State == PlayerState.GameOver)
        {
            return;
        }

        if (State.State == state && State.Direction == direction)
        {
            return;
        }

        if (state == PlayerState.None && direction == PlayerStateDirection.None)
        {
            SpriteSheet.ActivateAnimation(null);
        }

        else if (state == PlayerState.GameOver)
        {
            SpriteSheet.ActivateAnimation(Enum.GetName(state));
        }
        else
        {
            var animationName = Enum.GetName(state) + Enum.GetName(direction);
            SpriteSheet.ActivateAnimation(animationName);
        }

        State = (state, direction);
    }

    public void GameOver()
    {
        SetState(PlayerState.GameOver, PlayerStateDirection.None);
    }

    public void Attack()
    {
        if (State.State == PlayerState.GameOver)
        {
            return;
        }

        var direction = State.Direction;
        SetState(PlayerState.Attack, direction);
    }

    public void UpdatePosition(double up, double down, double left, double right, int width, int height, double time, Rectangle<int> worldBounds)
    {
        if (State.State == PlayerState.GameOver)
        {
            return;
        }

        var pixelsToMove = _speed * (time / 1000.0);

        int newX = Position.X + (int)(right * pixelsToMove) - (int)(left * pixelsToMove);
        int newY = Position.Y + (int)(down * pixelsToMove) - (int)(up * pixelsToMove);

        // Clamp to bounds
        newX = Math.Clamp(newX, worldBounds.Origin.X, worldBounds.Origin.X + worldBounds.Size.X - 1);
        newY = Math.Clamp(newY, worldBounds.Origin.Y, worldBounds.Origin.Y + worldBounds.Size.Y - 1);

        var newState = State.State;
        var newDirection = State.Direction;

        if (newX == Position.X && newY == Position.Y)
        {
            if (State.State == PlayerState.Attack && SpriteSheet.AnimationFinished)
            {
                newState = PlayerState.Idle;
            }
            else
            {
                newState = PlayerState.Idle;
            }
        }
        else
        {
            newState = PlayerState.Move;

            if (newY < Position.Y) newDirection = PlayerStateDirection.Up;
            if (newY > Position.Y) newDirection = PlayerStateDirection.Down;
            if (newX < Position.X) newDirection = PlayerStateDirection.Left;
            if (newX > Position.X) newDirection = PlayerStateDirection.Right;
        }

        if (newState != State.State || newDirection != State.Direction)
        {
            SetState(newState, newDirection);
        }

        Position = (newX, newY);
    }

    public void KnockbackFrom((int X, int Y) source)
    {
        const int knockbackDistance = 16;

        int dx = Position.X - source.X;
        int dy = Position.Y - source.Y;

        // Normalize direction
        int xDirection = Math.Sign(dx);
        int yDirection = Math.Sign(dy);

        // Apply knockback
        int newX = Position.X + xDirection * knockbackDistance;
        int newY = Position.Y + yDirection * knockbackDistance;

        Position = (newX, newY);
    }
}