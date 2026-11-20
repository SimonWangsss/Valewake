using Microsoft.Xna.Framework;
using StardewValley;
using Valewake;

int count = 0;
void Check(bool result, string name) { count++; if (!result) throw new Exception(name); }
(NPC Npc, MeetingPathController Controller, List<Rectangle> Rejected) Setup(float nextX, float nextY,
    Func<MeetingTile, bool> tile, Func<Rectangle, bool> body)
{
    NPC npc = new() { Position = new(0, 0), PendingPosition = new(nextX, nextY) };
    List<Rectangle> rejected = new();
    MeetingPathController controller = new(new[] { new MeetingTile(0, 0), new MeetingTile(1, 0) },
        npc.currentLocation, npc, tile, body, box => rejected.Add(box));
    npc.controller = controller;
    return (npc, controller, rejected);
}
var safe = Setup(8, 0, _ => true, _ => true);
safe.Controller.update(new());
Check(safe.Npc.Position.X == 8 && safe.Rejected.Count == 0, "valid accelerated movement preserved");
Check(safe.Controller.NPCSchedule, "offscreen position shortcut remains disabled");
Check(!safe.Controller.NativeWarpAllowedDuringMove, "native automatic map warp disabled during move");

var body = Setup(12, 0, _ => true, box => box.X + box.Width <= 64);
body.Controller.update(new());
Check(body.Npc.Position.X == 0, "whole body crossing water edge rolled back while tile center remains safe");
Check(body.Npc.Halted && body.Npc.controller is null, "failed movement controller detached");
Check(body.Rejected.Count == 1, "blocked footprint reported once");

var thin = Setup(12, 0, _ => true, box => box.X != 12);
thin.Controller.update(new());
Check(thin.Npc.Position.X == 0, "intermediate barrier cannot be jumped over");

var waypoint = Setup(8, 0, _ => false, _ => true);
waypoint.Controller.update(new());
Check(waypoint.Npc.Position.X == 0 && waypoint.Rejected.Count == 1, "invalid next tile prevents movement");

var jump = Setup(256, 0, _ => true, _ => true);
jump.Controller.update(new());
Check(jump.Npc.Position.X == 0, "unexpected native position jump rejected");

var vertical = Setup(0, 8, _ => true, box => box.Y + box.Height <= 52);
vertical.Controller.update(new());
Check(vertical.Npc.Position.Y == 0, "vertical footprint crossing step boundary rejected");
var talking = Setup(0, 0, _ => true, _ => true);
talking.Npc.movementPause = 3000;
talking.Npc.faceTowardFarmerTimer = 3000;
NpcActivityReset.ReleaseConversationPause(talking.Npc, new StardewModdingAPI.Helper());
Check(talking.Npc.movementPause == 0 && talking.Npc.faceTowardFarmerTimer == 0, "conversation pause and face timer cleared before walking");
Check(!talking.Npc.freezeMotion && !talking.Npc.faceTowardFarmer, "conversation freeze no longer overrides controller");
Check(talking.Npc.Position.X == 0 && talking.Npc.Position.Y == 0, "releasing conversation does not teleport NPC");
Check(ReferenceEquals(talking.Npc.controller, talking.Controller), "releasing conversation preserves owning controller");
Console.WriteLine($"Meeting movement contract: {count}/{count} passed (real guard and pause release, substituted native mover; not live maps).");
