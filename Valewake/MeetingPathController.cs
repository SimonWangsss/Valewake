using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Pathfinding;

namespace Valewake;

internal sealed class MeetingPathController : PathFindController
{
    private readonly NPC npc;
    private readonly Func<MeetingTile, bool> canWalk;
    private readonly Action<Rectangle> onBlocked;
    private readonly Func<Rectangle, bool> canOccupy;

    public MeetingPathController(IReadOnlyList<MeetingTile> path, GameLocation map, NPC npc,
        Func<MeetingTile, bool> canWalk, Func<Rectangle, bool> canOccupy, Action<Rectangle> onBlocked)
        : base(new Stack<Point>(path.Reverse().Select(p => new Point(p.X, p.Y))), map, npc,
            new Point(path[^1].X, path[^1].Y))
    {
        this.npc = npc;
        this.canWalk = canWalk;
        this.onBlocked = onBlocked;
        this.canOccupy = canOccupy;
        // Keep walking offscreen instead of using the base controller's position shortcut.
        NPCSchedule = true;
    }

    public override bool update(GameTime time)
    {
        if (pathToEndPoint?.Count > 0)
        {
            Point next = pathToEndPoint.Peek();
            if (!canWalk(new(next.X, next.Y))) return Stop(new Rectangle(next.X * 64, next.Y * 64, 64, 64));
        }
        Vector2 before = npc.Position;
        Rectangle beforeBox = npc.GetBoundingBox();
        GameLocation map = npc.currentLocation;
        bool done = base.update(time);
        if (npc.currentLocation == map)
        {
            Rectangle afterBox = npc.GetBoundingBox();
            Rectangle rejected = afterBox;
            if (!MeetingRules.ValidateSweep(beforeBox.X, beforeBox.Y, afterBox.X, afterBox.Y, (x, y) =>
            {
                Rectangle sample = afterBox;
                sample.X = x;
                sample.Y = y;
                if (canOccupy(sample)) return true;
                rejected = sample;
                return false;
            }))
            {
                npc.Position = before;
                return Stop(rejected);
            }
        }
        return done;
    }

    protected override void moveCharacter(GameTime time)
    {
        // Map transitions belong to MeetingManager, not the controller's schedule warp shortcut.
        NPCSchedule = false;
        try { base.moveCharacter(time); }
        finally { NPCSchedule = true; }
    }

    private bool Stop(Rectangle rejected)
    {
        npc.Halt();
        npc.controller = null;
        onBlocked(rejected);
        return true;
    }
}
