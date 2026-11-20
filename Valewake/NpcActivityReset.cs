using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;

namespace Valewake;

internal static class NpcActivityReset
{
    public static void ReleaseConversationPause(NPC npc, IModHelper helper)
    {
        npc.movementPause = 0;
        npc.faceTowardFarmerTimer = 0;
        helper.Reflection.GetField<bool>(npc, "faceTowardFarmer").SetValue(false);
        helper.Reflection.GetField<bool>(npc, "freezeMotion").SetValue(false);
    }

    public static void Stop(NPC npc, IModHelper helper)
    {
        // Finish the game's route behavior, including frozen motion and sprite offsets.
        helper.Reflection.GetMethod(npc, "finishEndOfRouteAnimation").Invoke();
        helper.Reflection.GetMethod(npc, "routeEndAnimationFinished").Invoke(new object[] { null! });
        helper.Reflection.GetField<bool>(npc, "currentlyDoingEndOfRouteAnimation").SetValue(false);
        helper.Reflection.GetField<bool>(npc, "isWalkingInSquare").SetValue(false);
        helper.Reflection.GetField<bool>(npc, "returningToEndPoint").SetValue(false);
        npc.controller = null;
        npc.temporaryController = null;
        npc.Halt();
        npc.Sprite.ClearAnimation();
        helper.Reflection.GetField<bool>(npc, "freezeMotion").SetValue(false);
        npc.drawOffset = Vector2.Zero;
        npc.faceDirection(2);
    }
}
