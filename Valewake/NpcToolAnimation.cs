using System.Collections.Generic;
using Microsoft.Xna.Framework;
using StardewValley;

namespace Valewake;

public enum NpcToolKind
{
    Pickaxe,
    Axe
}

public static class NpcToolAnimation
{
    private const int PickaxeTileIndex = 105;
    private const int AxeTileIndex = 189;

    public static void Play(NPC npc, NpcToolKind tool)
    {
        PlayBodySwing(npc);
        int tileIndex = tool == NpcToolKind.Pickaxe ? PickaxeTileIndex : AxeTileIndex;
        int textureWidth = Game1.toolSpriteSheet.Width;
        Rectangle source = new(
            tileIndex * 16 % textureWidth,
            tileIndex * 16 / textureWidth * 16,
            16,
            32
        );
        AddToolFrames(npc, source);
    }

    public static void PlayBodySwing(NPC npc)
    {
        int frame = npc.FacingDirection switch { 1 => 5, 0 => 9, 3 => 13, _ => 1 };
        npc.Sprite.setCurrentAnimation(new List<FarmerSprite.AnimationFrame>
        {
            new(frame, 90),
            new(frame + 2, 110),
            new(frame, 90)
        });
    }

    private static void AddToolFrames(NPC npc, Rectangle source)
    {
        (Vector2 offset, bool flipped, float start, float end) = npc.FacingDirection switch
        {
            1 => (new Vector2(30f, -54f), false, -0.9f, 0.85f),
            3 => (new Vector2(-30f, -54f), true, 0.9f, -0.85f),
            0 => (new Vector2(0f, -72f), false, -0.75f, 0.75f),
            _ => (new Vector2(0f, -34f), false, -0.9f, 0.9f)
        };

        for (int step = 0; step < 3; step++)
        {
            float amount = step / 2f;
            TemporaryAnimatedSprite sprite = new(
                "TileSheets\\tools",
                source,
                95f,
                1,
                0,
                offset,
                flicker: false,
                flipped,
                -1f,
                0f,
                Color.White,
                4f,
                0f,
                MathHelper.Lerp(start, end, amount),
                0f
            )
            {
                attachedCharacter = npc,
                positionFollowsAttachedCharacter = true,
                delayBeforeAnimationStart = step * 85,
                layerDepthOffset = 0.002f
            };
            npc.currentLocation.TemporarySprites.Add(sprite);
        }
    }
}
