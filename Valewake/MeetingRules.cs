using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Valewake;

public readonly record struct MeetingTile(int X, int Y);

/// <summary>Pure rules shared by the game adapter and offline regression tests.</summary>
public static class MeetingRules
{
    public static int FollowSpeed(float playerSpeed, int distance, int boost, int walkCap, int catchUpCap)
    {
        int cap = distance >= 8 ? Math.Clamp(catchUpCap, 2, 6) : Math.Clamp(walkCap, 2, 5);
        return Math.Clamp((int)Math.Round(playerSpeed) + Math.Clamp(boost, 0, 2) + (distance >= 8 ? 1 : 0), 2, cap);
    }

    public static int IdleFacing(int dx, int dy) => Math.Abs(dx) > Math.Abs(dy) && dx != 0 ? dx > 0 ? 1 : 3 : 2;

    public static bool RecoveryGracePassed(long startedTick, long now, int graceTicks) =>
        startedTick > 0 && now - startedTick >= Math.Max(60, graceTicks);

    public static bool CanTransferToMine(string state, bool owner, bool hasReturnContext) =>
        owner && hasReturnContext && (state is "active" or "returning");

    public static int LatestScheduleTime(IEnumerable<int> times, int now) => times.Where(t => t <= now).DefaultIfEmpty(-1).Max();

    public static int MapDistance(string source, string destination, Func<string, IEnumerable<string>> exits, string forbidden = "")
    {
        Queue<(string Map, int Distance)> queue = new();
        HashSet<string> seen = new(StringComparer.Ordinal);
        queue.Enqueue((source, 0));
        while (queue.Count > 0 && seen.Count < 100)
        {
            var current = queue.Dequeue();
            if (current.Map == forbidden || !seen.Add(current.Map)) continue;
            if (current.Map == destination) return current.Distance;
            foreach (string next in exits(current.Map)) queue.Enqueue((next, current.Distance + 1));
        }
        return int.MaxValue;
    }

    public static IEnumerable<MeetingTile> ExitApproaches(MeetingTile gate, int width, int height,
        Func<MeetingTile, bool> reachable, Func<MeetingTile, bool> corridor)
    {
        bool boundary = gate.X <= 1 || gate.Y <= 1 || gate.X >= width - 2 || gate.Y >= height - 2;
        int radius = boundary ? 6 : 2;
        return Enumerable.Range(-radius, 2 * radius + 1).SelectMany(dx =>
                Enumerable.Range(-radius, 2 * radius + 1).Select(dy => new MeetingTile(gate.X + dx, gate.Y + dy)))
            .Where(p => p.X >= 1 && p.Y >= 1 && p.X < width - 1 && p.Y < height - 1 &&
                (p.X == gate.X || p.Y == gate.Y) && Distance(p, gate) <= radius && reachable(p))
            .Where(p => Enumerable.Range(1, Math.Max(0, Distance(p, gate) - 1)).All(step =>
            {
                MeetingTile middle = new(p.X + Math.Sign(gate.X - p.X) * step, p.Y + Math.Sign(gate.Y - p.Y) * step);
                return middle.X < 0 || middle.Y < 0 || middle.X >= width || middle.Y >= height || corridor(middle);
            }))
            .OrderBy(p => Distance(p, gate));
    }

    public static string MineRejectionReply(string reason, bool chinese = true)
    {
        if (!chinese) return reason.Contains("already active") ? "I'm already accompanying you. Let's keep going." : "I can't head into the mines with you just now. Shall we arrange it a little later?";
        if (reason.Contains("already active")) return "我已经在陪你下矿了，我们接着走吧。";
        if (reason.Contains("hearts") || reason.Contains("trust")) return "矿洞里还是有些危险，我想等我们更熟悉一点再一起去。";
        if (reason.Contains("too late")) return "今天有些晚了，我们留到早点的时候再下矿吧。";
        if (reason.Contains("reserved") || reason.Contains("reservation")) return "我还有别的安排没处理好，等这边结束再一起下矿，好吗？";
        return "这会儿还不方便一起下矿，我们稍后再商量，好吗？";
    }

    public static string? ResolveMeetingLocation(string name)
    {
        string n = (name ?? "").Trim().ToLowerInvariant();
        if (Regex.IsMatch(n, "(?:矿洞|矿井|商店|店|房子|家)(?:里面|里|内)|inside|saloon|seedshop|leahhouse|robinhouse")) return null;
        if (n == "town" || Regex.IsMatch(n, "镇上|镇子|鹈鹕镇")) return "Town";
        if (n == "farm" || n.Contains("农场")) return "Farm";
        if (n == "beach" || Regex.IsMatch(n, "海边|海滩|beach")) return "Beach";
        if (n == "forest" || Regex.IsMatch(n, "森林|forest")) return "Forest";
        if (n == "mountain" || Regex.IsMatch(n, "山上|山地|山路|山湖|mountain")) return "Mountain";
        bool outside = Regex.IsMatch(n, "附近|边上|门口|外面|外边|入口|下面|旁|near|outside|entrance");
        if (outside && Regex.IsMatch(n, "矿洞|矿井|罗宾|木匠|mine|robin|carpenter")) return "Mountain";
        if (outside && Regex.IsMatch(n, "皮埃尔|杂货店|酒馆|pierre|store")) return "Town";
        return null;
    }

    public static string RejectionReply(string reason, bool chinese)
    {
        if (!chinese) return "That arrangement won't work for me just now. Shall we choose another time or place?";
        if (reason.Contains("位置") || reason.Contains("地方")) return "那个地方这次不太方便，我们换个方便碰面的地方，好吗？";
        if (reason.Contains("分钟") || reason.Contains("时间")) return "这个时间有点赶，我们另约个宽裕一点的时间吧，好吗？";
        if (reason.Contains("节日") || reason.Contains("活动")) return "那会儿还有活动，等忙完了我们再约，好吗？";
        if (reason.Contains("scheduled") || reason.Contains("安排")) return "我还有别的安排没结束，等这边忙完再约，好吗？";
        if (reason.Contains("hearts") || reason.Contains("trust")) return "我想我们还是先多熟悉一点，再约着一起出去吧。";
        return "这次我还不能按这个安排来，我们再商量一下，好吗？";
    }
    public static bool MeetingDue(int day, int scheduledDay, int now, int scheduledTime) =>
        day == scheduledDay && now >= scheduledTime && !WindowExpired(day, scheduledDay, now, scheduledTime);

    public static bool CatchUpDue(bool sameMap, int distance, long stalledTicks, long cooldownTicks,
        int farDistance, int stallLimit, int crossMapLimit, int cooldownLimit) =>
        cooldownTicks >= Math.Max(60, cooldownLimit) &&
        (!sameMap ? stalledTicks >= Math.Max(30, crossMapLimit) :
            distance >= Math.Max(8, farDistance) || distance > 3 && stalledTicks >= Math.Max(60, stallLimit));

    public static bool BlocksWater(bool water, bool passableDeck) => water && !passableDeck;

    public static bool ValidateSweep(int fromX, int fromY, int toX, int toY, Func<int, int, bool> valid)
    {
        int length = Math.Max(Math.Abs(toX - fromX), Math.Abs(toY - fromY));
        if (length > 128) return false;
        int steps = Math.Max(1, (int)Math.Ceiling(length / 4d));
        for (int i = 1; i <= steps; i++)
            if (!valid(fromX + (toX - fromX) * i / steps, fromY + (toY - fromY) * i / steps)) return false;
        return true;
    }

    public static IEnumerable<MeetingTile> RecoveryTiles(MeetingTile player, Func<MeetingTile, bool> ground, Func<MeetingTile, bool> blocked) =>
        Reachable(player, ground).Where(p => Distance(p, player) is >= 2 and <= 4 && !blocked(p));

    public static IEnumerable<MeetingTile> FootprintTiles(int left, int top, int width, int height)
    {
        for (int y = (int)Math.Floor(top / 64d); y <= (int)Math.Floor((top + height - 1) / 64d); y++)
            for (int x = (int)Math.Floor(left / 64d); x <= (int)Math.Floor((left + width - 1) / 64d); x++)
                yield return new(x, y);
    }
    public static bool IsEndRequest(string text)
    {
        if (Regex.IsMatch(text, "不要|不想|不回去|不回家|别结束|不结束|结束后|结束以后|如果|要是|明天|后天|什么时候|几点结束|几时结束")) return false;
        if (Regex.IsMatch(text, @"(?:^|[，,。！!])\s*(?:今天(?:就|先)?聊到这里吧|你(?:早点|先)回去(?:吧)?)(?:[，,。！!]|$)")) return true;
        return Regex.IsMatch(text.Trim(),
        @"^(?:我们)?(?:今天的)?(?:约会|会面|见面)?(?:就)?(?:结束(?:约会|会面)?(?:了|吧)?|到这里(?:吧)?)[。！!\s]*$|^(?:结束约会|结束会面|今天就到这里(?:吧)?|我们回去吧|你先回去吧)[。！!\s]*$|^(?:end (?:the )?(?:date|meeting)|let's end (?:the )?date|let's head back)[.!\s]*$",
        RegexOptions.IgnoreCase) || Regex.IsMatch(text.Trim(),
            @"^(?:那|嗯|好|好的|那好)?[，,\s]*(?:再见|拜拜|晚安|bye|goodbye|good night)[。！!\s]*$|^(?:那)?(?:今天|我们|我先|你先).*(?:回去|回家|回去了|到这里)(?:吧|了)?[，,。！!\s]*(?:时候不早了|天不早了|晚安)?[。！!\s]*$",
            RegexOptions.IgnoreCase);
    }

    public static bool IsFishing(string topic) => Regex.IsMatch(topic, "钓|fish", RegexOptions.IgnoreCase);

    public static bool IsJoinRequest(string text) => !Regex.IsMatch(text, "不要|不想|如果|明天|后天|一起.*吗") &&
        Regex.IsMatch(text.Trim(), @"^(?:那|好|那好)?[，,\s]*(?:一起过去吧|我们一起过去吧|一起走吧|我们一起走吧|我陪你过去|我跟你一起过去|let's go together)[。！!\s]*$", RegexOptions.IgnoreCase);

    public static bool WindowExpired(int currentDay, int scheduledDay, int now, int time) =>
        currentDay > scheduledDay || currentDay == scheduledDay && Minutes(now) >= Minutes(time) + 120;

    public static int Distance(MeetingTile a, MeetingTile b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

    public static string? FirstHop(string source, string destination, Func<string, IEnumerable<string>> exits)
    {
        Queue<(string Map, string? First)> queue = new();
        HashSet<string> seen = new(StringComparer.Ordinal);
        queue.Enqueue((source, null));
        while (queue.Count > 0 && seen.Count < 100)
        {
            var current = queue.Dequeue();
            if (!seen.Add(current.Map)) continue;
            if (current.Map == destination) return current.First;
            foreach (string next in exits(current.Map))
                if (!seen.Contains(next)) queue.Enqueue((next, current.First ?? next));
        }
        return null;
    }

    public static int Minutes(int hhmm) => hhmm / 100 * 60 + hhmm % 100;

    public static bool ValidTime(int time) => time >= 800 && time <= 2200 && time % 100 < 60 && time % 10 == 0;

    public static bool CanSchedule(int now, int time, int dayOffset, int leadMinutes) =>
        ValidTime(time) && dayOffset >= 0 && dayOffset <= 7 &&
        (dayOffset > 0 || Minutes(time) - Minutes(now) >= Math.Max(10, leadMinutes));

    public static IEnumerable<MeetingTile> Neighbors(MeetingTile p)
    {
        yield return new(p.X + 1, p.Y);
        yield return new(p.X - 1, p.Y);
        yield return new(p.X, p.Y + 1);
        yield return new(p.X, p.Y - 1);
    }

    public static List<MeetingTile>? FindPath(MeetingTile start, MeetingTile end, Func<MeetingTile, bool> walkable, int budget = 16000)
    {
        if (!walkable(start) || !walkable(end)) return null;
        Dictionary<MeetingTile, MeetingTile> previous = new() { [start] = start };
        Queue<MeetingTile> queue = new();
        queue.Enqueue(start);
        while (queue.Count > 0 && previous.Count <= budget)
        {
            MeetingTile p = queue.Dequeue();
            if (p == end)
            {
                List<MeetingTile> path = new() { end };
                while (path[^1] != start) path.Add(previous[path[^1]]);
                path.Reverse();
                return path;
            }
            foreach (MeetingTile next in Neighbors(p))
                if (!previous.ContainsKey(next) && walkable(next)) { previous[next] = p; queue.Enqueue(next); }
        }
        return null;
    }

    public static HashSet<MeetingTile> Reachable(MeetingTile start, Func<MeetingTile, bool> walkable, int budget = 16000)
    {
        HashSet<MeetingTile> visited = new();
        if (!walkable(start)) return visited;
        Queue<MeetingTile> queue = new();
        queue.Enqueue(start);
        visited.Add(start);
        while (queue.Count > 0 && visited.Count < budget)
        {
            foreach (MeetingTile next in Neighbors(queue.Dequeue()))
                if (!visited.Contains(next) && walkable(next))
                {
                    visited.Add(next);
                    queue.Enqueue(next);
                }
        }
        return visited;
    }

    public static bool HasApproach(MeetingTile candidate, MeetingTile start,
        Func<MeetingTile, bool> walkable)
    {
        // The player must be able to reach a neighboring tile after the NPC occupies the candidate.
        HashSet<MeetingTile> reachable = Reachable(start, p => p != candidate && walkable(p));
        return Neighbors(candidate).Any(reachable.Contains);
    }
}
