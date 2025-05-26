using System.Reflection;
using System.Text.Json;
using System;
using Silk.NET.Maths;
using TheAdventure.Models;
using TheAdventure.Models.Data;
using TheAdventure.Scripting;

namespace TheAdventure;

public class Engine
{
    private readonly GameRenderer _renderer;
    private readonly Input _input;
    private readonly ScriptEngine _scriptEngine = new();

    private readonly Dictionary<int, GameObject> _gameObjects = new();
    private readonly Dictionary<string, TileSet> _loadedTileSets = new();
    private readonly Dictionary<int, Tile> _tileIdMap = new();

    private Level _currentLevel = new();
    private PlayerObject? _player;

    private DateTimeOffset _lastUpdate = DateTimeOffset.Now;

    private int _heartFullId;
    private int _heartEmptyId;

    private int _youDiedTextureId;

    
    private readonly Random _random = new();
    private double _enemySpawnAccumulator = 0;
    private const double EnemySpawnInterval = 5000;
    private const int EnemySpawnRadius = 300;

    private double _healthSpawnAccumulator = 0;
    private const double HealthSpawnInterval = 8000;
    private const int MaxHealthPickups = 5;

    private SpriteSheet? _enemySheet;


    public Engine(GameRenderer renderer, Input input)
    {
        _renderer = renderer;
        _input = input;

        _input.OnMouseClick += (_, coords) => AddBomb(coords.x, coords.y);
    }

    public void SetupWorld()
    {
        _player = new(SpriteSheet.Load(_renderer, "Player.json", "Assets"), 100, 100);
        _enemySheet = SpriteSheet.Load(_renderer, "Enemy.json", "Assets");

        var levelContent = File.ReadAllText(Path.Combine("Assets", "terrain.tmj"));
        var level = JsonSerializer.Deserialize<Level>(levelContent);
        if (level == null)
        {
            throw new Exception("Failed to load level");
        }

        foreach (var tileSetRef in level.TileSets)
        {
            var tileSetContent = File.ReadAllText(Path.Combine("Assets", tileSetRef.Source));
            var tileSet = JsonSerializer.Deserialize<TileSet>(tileSetContent);
            if (tileSet == null)
            {
                throw new Exception("Failed to load tile set");
            }

            foreach (var tile in tileSet.Tiles)
            {
                tile.TextureId = _renderer.LoadTexture(Path.Combine("Assets", tile.Image), out _);
                _tileIdMap.Add(tile.Id!.Value, tile);
            }

            _loadedTileSets.Add(tileSet.Name, tileSet);
        }

        if (level.Width == null || level.Height == null)
        {
            throw new Exception("Invalid level dimensions");
        }

        if (level.TileWidth == null || level.TileHeight == null)
        {
            throw new Exception("Invalid tile dimensions");
        }

        _heartFullId = _renderer.LoadTexture(Path.Combine("Assets", "heart_full.png"), out _);
        _heartEmptyId = _renderer.LoadTexture(Path.Combine("Assets", "heart_empty.png"), out _);

        _youDiedTextureId = _renderer.LoadTexture(Path.Combine("Assets", "you_died.png"), out _);

        _renderer.SetWorldBounds(new Rectangle<int>(0, 0, level.Width.Value * level.TileWidth.Value,
            level.Height.Value * level.TileHeight.Value));

        _currentLevel = level;

        _scriptEngine.LoadAll(Path.Combine("Assets", "Scripts"));
    }

    public void ProcessFrame()
    {
        var currentTime = DateTimeOffset.Now;
        var msSinceLastFrame = (currentTime - _lastUpdate).TotalMilliseconds;
        _lastUpdate = currentTime;

        if (_player == null)
        {
            return;
        }

        if (_player.State.State == PlayerObject.PlayerState.GameOver)
        {
            return;
        }

        double up = _input.IsUpPressed() ? 1.0 : 0.0;
        double down = _input.IsDownPressed() ? 1.0 : 0.0;
        double left = _input.IsLeftPressed() ? 1.0 : 0.0;
        double right = _input.IsRightPressed() ? 1.0 : 0.0;
        bool isAttacking = _input.IsKeyAPressed() && (up + down + left + right <= 1);
        bool addBomb = _input.IsKeyBPressed();

        var worldWidth = _currentLevel.Width ?? throw new Exception("Level width is null");
        var worldHeight = _currentLevel.Height ?? throw new Exception("Level height is null");
        var tileWidth = _currentLevel.TileWidth ?? throw new Exception("Tile width is null");
        var tileHeight = _currentLevel.TileHeight ?? throw new Exception("Tile height is null");

        var worldBounds = new Rectangle<int>(0, 0, worldWidth * tileWidth, worldHeight * tileHeight);

        _player.UpdatePosition(up, down, left, right, 48, 48, msSinceLastFrame, worldBounds);

        if (isAttacking)
        {
            _player.Attack();
        }

        // Handle enemy spawning
        _enemySpawnAccumulator += msSinceLastFrame;
        if (_enemySpawnAccumulator >= EnemySpawnInterval)
        {
            _enemySpawnAccumulator -= EnemySpawnInterval;
            SpawnEnemyAroundPlayer();
        }

        _healthSpawnAccumulator += msSinceLastFrame;
        if (_healthSpawnAccumulator >= HealthSpawnInterval)
        {
            _healthSpawnAccumulator -= HealthSpawnInterval;
            SpawnHealthPickup();
        }

        // Update all enemies
        foreach (var obj in _gameObjects.Values)
        {
            if (obj is Enemy enemy)
            {
                enemy.Update(this, msSinceLastFrame);
            }
        }

        // Remove enemies that dealt damage
        var enemiesToRemove = _gameObjects
            .Where(pair => pair.Value is Enemy e && e.ShouldBeRemoved)
            .Select(pair => pair.Key)
            .ToList();

        foreach (var id in enemiesToRemove)
        {
            _gameObjects.Remove(id);
        }

        _scriptEngine.ExecuteAll(this);

        if (addBomb && _player.State.State != PlayerObject.PlayerState.GameOver)
        {
            AddBomb(_player.Position.X, _player.Position.Y, false);
        }
    }


    public void RenderFrame()
    {
        _renderer.SetDrawColor(0, 0, 0, 255);
        _renderer.ClearScreen();

        var playerPosition = _player!.Position;
        _renderer.CameraLookAt(playerPosition.X, playerPosition.Y);

        RenderTerrain();
        RenderAllObjects();
        RenderPlayerHealth();

        if (_player != null && _player.State.State == PlayerObject.PlayerState.GameOver)
        {
            RenderGameOverOverlay();
        }

        _renderer.PresentFrame();
    }

    private void RenderPlayerHealth()
    {
        if (_player == null)
            return;

        int padding = 10;
        int spacing = 48;
        for (int i = 0; i < 3; i++)
        {
            var textureId = i < _player.Health ? _heartFullId : _heartEmptyId;
            var dstRect = new Rectangle<int>(padding + i * spacing, padding, 32, 32);
            _renderer.RenderUITexture(textureId, new Rectangle<int>(0, 0, 32, 32), dstRect);
        }
    }

    public void RenderAllObjects()
    {
        var tempToRemove = new List<int>();
        var pickupsToRemove = new List<int>();

        foreach (var gameObject in GetRenderables())
        {
            gameObject.Render(_renderer);

            if (gameObject is TemporaryGameObject { IsExpired: true } tempGameObject)
            {
                tempToRemove.Add(tempGameObject.Id);
            }
            else if (gameObject is HealthPickup healthPickup && _player != null)
            {
                int deltaX = Math.Abs(_player.Position.X - healthPickup.Position.X);
                int deltaY = Math.Abs(_player.Position.Y - healthPickup.Position.Y);

                if (deltaX < 32 && deltaY < 32 && _player.Health < 3)
                {
                    _player.Heal(1);
                    pickupsToRemove.Add(healthPickup.Id);
                }
            }
        }

        foreach (var id in tempToRemove)
        {
            _gameObjects.Remove(id, out var gameObject);

            if (gameObject is TemporaryGameObject temp && _player != null)
            {
                var deltaX = Math.Abs(_player.Position.X - temp.Position.X);
                var deltaY = Math.Abs(_player.Position.Y - temp.Position.Y);
                if (deltaX < 32 && deltaY < 32)
                {
                    _player.LoseHealth();
                    _player.KnockbackFrom(temp.Position);
                }
            }
        }

        foreach (var id in pickupsToRemove)
        {
            _gameObjects.Remove(id);
        }

        _player?.Render(_renderer);
    }


    private void SpawnEnemyAroundPlayer()
    {
        var (playerX, playerY) = _player!.Position;

        var angle = _random.NextDouble() * Math.PI * 2;
        var distance = _random.NextDouble() * EnemySpawnRadius;

        var offsetX = (int)(Math.Cos(angle) * distance);
        var offsetY = (int)(Math.Sin(angle) * distance);
        var spawnX = playerX + offsetX;
        var spawnY = playerY + offsetY;

        var enemy = new Enemy(_enemySheet!, (spawnX, spawnY));
        _gameObjects.Add(enemy.Id, enemy);
    }

    private void SpawnHealthPickup()
    {
        if (_player == null) return;

        int activeHealthCount = _gameObjects.Values.Count(o => o is HealthPickup);
        if (activeHealthCount >= MaxHealthPickups)
            return;

        var (playerX, playerY) = _player.Position;

        var angle = _random.NextDouble() * Math.PI * 2;
        var distance = _random.Next(100, 300);

        var offsetX = (int)(Math.Cos(angle) * distance);
        var offsetY = (int)(Math.Sin(angle) * distance);

        var spawnX = playerX + offsetX;
        var spawnY = playerY + offsetY;

        var pickup = new HealthPickup(_heartFullId, (spawnX, spawnY));
        _gameObjects.Add(pickup.Id, pickup);
    }

    public void RenderTerrain()
    {
        foreach (var currentLayer in _currentLevel.Layers)
        {
            for (int i = 0; i < _currentLevel.Width; ++i)
            {
                for (int j = 0; j < _currentLevel.Height; ++j)
                {
                    int? dataIndex = j * currentLayer.Width + i;
                    if (dataIndex == null)
                    {
                        continue;
                    }

                    var currentTileId = currentLayer.Data[dataIndex.Value] - 1;
                    if (currentTileId == null)
                    {
                        continue;
                    }

                    var currentTile = _tileIdMap[currentTileId.Value];

                    var tileWidth = currentTile.ImageWidth ?? 0;
                    var tileHeight = currentTile.ImageHeight ?? 0;

                    var sourceRect = new Rectangle<int>(0, 0, tileWidth, tileHeight);
                    var destRect = new Rectangle<int>(i * tileWidth, j * tileHeight, tileWidth, tileHeight);
                    _renderer.RenderTexture(currentTile.TextureId, sourceRect, destRect);
                }
            }
        }
    }

    private void RenderGameOverOverlay()
    {
        var screenWidth = _renderer.ScreenWidth;
        var screenHeight = _renderer.ScreenHeight;

        int overlayWidth = 256;
        int overlayHeight = 64;

        var dstRect = new Rectangle<int>(
            (screenWidth - overlayWidth) / 2,
            (screenHeight - overlayHeight) / 2,
            overlayWidth, overlayHeight
        );

        _renderer.RenderUITexture(_youDiedTextureId, new Rectangle<int>(0, 0, overlayWidth, overlayHeight), dstRect);
    }


    public IEnumerable<RenderableGameObject> GetRenderables()
    {
        foreach (var gameObject in _gameObjects.Values)
        {
            if (gameObject is RenderableGameObject renderableGameObject)
            {
                yield return renderableGameObject;
            }
        }
    }

    public (int X, int Y) GetPlayerPosition()
    {
        return _player!.Position;
    }

    public void DamagePlayerFrom((int X, int Y) enemyPosition)
    {
        if (_player == null || _player.State.State == PlayerObject.PlayerState.GameOver)
            return;

        _player.LoseHealth();
        _player.KnockbackFrom(enemyPosition);
    }
 
    public void AddBomb(int X, int Y, bool translateCoordinates = true)
    {
        var worldCoords = translateCoordinates ? _renderer.ToWorldCoordinates(X, Y) : new Vector2D<int>(X, Y);

        SpriteSheet spriteSheet = SpriteSheet.Load(_renderer, "BombExploding.json", "Assets");
        spriteSheet.ActivateAnimation("Explode");

        TemporaryGameObject bomb = new(spriteSheet, 2.1, (worldCoords.X, worldCoords.Y));
        _gameObjects.Add(bomb.Id, bomb);
    }
}