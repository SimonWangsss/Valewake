namespace Microsoft.Xna.Framework
{
    public struct Vector2
    {
        public float X, Y;
        public Vector2(float x, float y) { X = x; Y = y; }
        public static Vector2 Zero => new(0, 0);
    }
    public readonly record struct Point(int X, int Y);
    public struct Rectangle
    {
        public int X, Y, Width, Height;
        public Rectangle(int x, int y, int width, int height) { X = x; Y = y; Width = width; Height = height; }
    }
    public sealed class GameTime { }
}
namespace StardewValley
{
    using Microsoft.Xna.Framework;
    public sealed class GameLocation { }
    public sealed class NPC
    {
        public Vector2 Position;
        public Vector2 PendingPosition;
        public Vector2 Tile => new((int)(Position.X + 32) / 64, (int)(Position.Y + 32) / 64);
        public GameLocation currentLocation = new();
        public object? controller;
        public bool Halted;
        public int movementPause, faceTowardFarmerTimer;
        public bool faceTowardFarmer = true, freezeMotion = true;
        public bool currentlyDoingEndOfRouteAnimation, isWalkingInSquare, returningToEndPoint;
        public object? temporaryController;
        public Vector2 drawOffset;
        public Sprite Sprite = new();
        public void faceDirection(int facing) { }
        public void Halt() => Halted = true;
        public Rectangle GetBoundingBox() => new((int)Position.X + 8, (int)Position.Y + 16, 48, 32);
    }
    public sealed class Sprite { public void ClearAnimation() { } }
}
namespace StardewModdingAPI
{
    public interface IModHelper { Reflection Reflection { get; } }
    public sealed class Helper : IModHelper { public Reflection Reflection { get; } = new(); }
    public sealed class Reflection
    {
        public Field<T> GetField<T>(object target, string name) => new(target, name);
        public Method GetMethod(object target, string name) => new();
    }
    public sealed class Field<T>
    {
        private readonly object target;
        private readonly string name;
        public Field(object target, string name) { this.target = target; this.name = name; }
        public void SetValue(T value) => target.GetType().GetField(name)!.SetValue(target, value);
    }
    public sealed class Method { public void Invoke(params object[] args) { } }
}
namespace StardewValley.Pathfinding
{
    using Microsoft.Xna.Framework;
    public class PathFindController
    {
        private readonly NPC npc;
        public Stack<Point> pathToEndPoint;
        public bool NPCSchedule;
        public bool NativeWarpAllowedDuringMove;
        public PathFindController(Stack<Point> path, GameLocation map, NPC npc, Point end) { pathToEndPoint = path; this.npc = npc; }
        public virtual bool update(GameTime time) { moveCharacter(time); return false; }
        protected virtual void moveCharacter(GameTime time) { NativeWarpAllowedDuringMove = NPCSchedule; npc.Position = npc.PendingPosition; }
    }
}
