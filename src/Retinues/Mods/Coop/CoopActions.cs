using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Retinues.Features.AutoJoin;
using Retinues.Features.Unlocks;
using Retinues.Game;
using Retinues.Game.Wrappers;
using Retinues.Managers;
using Retinues.Troops;
using Retinues.Troops.Save;
using Retinues.Utils;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;

namespace Retinues.Mods.Coop
{
    /// <summary>
    /// The troop edits a player's game sends to the co-op server, and how the server carries them out: as the
    /// sending player's hero (Player.ActAs is already in effect), through the same manager calls the editor makes,
    /// and only on troops of that player's own clan or the kingdom they rule.
    /// Op names and argument order are part of the protocol (CoopBridge.ProtocolVersion).
    /// </summary>
    [SafeClass]
    public static class CoopActions
    {
        public const string Equip = "equip"; // troop, set, slot, item ("" = none), allowPurchase
        public const string Unequip = "unequip"; // troop, set, slot
        public const string DeleteSet = "deleteSet"; // troop, set
        public const string Paste = "paste"; // srcTroop, srcSet, dstTroop, dstSet, slots ("" = all, else "0,1,..")
        public const string Skill = "skill"; // troop, skill, increment
        public const string AddUpgrade = "addUpgrade"; // troop, name
        public const string Convert = "convert"; // from, to, amount
        public const string RankUp = "rankUp"; // troop
        public const string Remove = "remove"; // troop
        public const string JoinCap = "joinCap"; // troop, cap
        public const string EnsureTroops = "ensureTroops"; // faction
        public const string Appearance = "appearance"; // troop, then CoopBridge.AppearanceArgs
        public const string UnlockCounts = "unlockCounts"; // "kill" | "discard", then item, count pairs

        // A single report cannot add more than this to one item's progress.
        private const int MaxReportedPerItem = 1000;

        public static (bool Ok, string Message) Run(Hero hero, string op, IList<string> a)
        {
            try
            {
                switch (op)
                {
                    case Equip:
                    {
                        var troop = OwnTroop(hero, a, 0);
                        var item = string.IsNullOrEmpty(Arg(a, 3)) ? null : new WItem(Arg(a, 3));
                        var res = EquipmentManager.TryEquip(
                            troop,
                            Int(a, 1),
                            (EquipmentIndex)Int(a, 2),
                            item,
                            Bool(a, 4)
                        );
                        return (res.Ok, res.Ok ? null : $"Could not equip: {res.Reason}.");
                    }
                    case Unequip:
                    {
                        var res = EquipmentManager.TryUnequip(
                            OwnTroop(hero, a, 0),
                            Int(a, 1),
                            (EquipmentIndex)Int(a, 2)
                        );
                        return (res.Ok, res.Ok ? null : $"Could not unequip: {res.Reason}.");
                    }
                    case DeleteSet:
                    {
                        var res = EquipmentManager.TryDeleteSet(OwnTroop(hero, a, 0), Int(a, 1));
                        return (res.Ok, res.Ok ? null : "Could not delete that equipment set.");
                    }
                    case Paste:
                    {
                        var source = Troop(Arg(a, 0)).Loadout.Get(Int(a, 1));
                        var target = OwnTroop(hero, a, 2).Loadout.Get(Int(a, 3));
                        var slots = string.IsNullOrEmpty(Arg(a, 4))
                            ? null
                            : new HashSet<EquipmentIndex>(
                                Arg(a, 4)
                                    .Split(',')
                                    .Select(s =>
                                        (EquipmentIndex)int.Parse(s, CultureInfo.InvariantCulture)
                                    )
                            );
                        var res = EquipmentManager.TryPasteEquipment(source, target, slots);
                        return (res.Ok, res.Ok ? null : $"Could not paste: {res.Reason}.");
                    }
                    case Skill:
                    {
                        var troop = OwnTroop(hero, a, 0);
                        var skill =
                            MBObjectManager.Instance.GetObject<SkillObject>(Arg(a, 1))
                            ?? throw new ArgumentException("Unknown skill " + Arg(a, 1));
                        var increment = Bool(a, 2);
                        if (
                            increment
                                ? !SkillManager.CanIncrementSkill(troop, skill)
                                : !SkillManager.CanDecrementSkill(troop, skill)
                        )
                            return (false, "That skill change is not allowed now.");
                        SkillManager.ModifySkill(troop, skill, increment);
                        return (true, null);
                    }
                    case AddUpgrade:
                    {
                        var troop = OwnTroop(hero, a, 0);
                        if (!UpgradeManager.CanAddUpgradeToTroop(troop))
                            return (false, "That troop cannot get another upgrade.");
                        UpgradeManager.AddUpgradeTarget(troop, Arg(a, 1));
                        return (true, null);
                    }
                    case Convert:
                    {
                        var from = Troop(Arg(a, 0));
                        var to = OwnTroop(hero, a, 1);
                        RetinueManager.Convert(from, to, Int(a, 2));
                        return (true, null);
                    }
                    case RankUp:
                        RetinueManager.RankUp(OwnTroop(hero, a, 0));
                        return (true, null);
                    case Remove:
                    {
                        var troop = OwnTroop(hero, a, 0);
                        if (!troop.IsDeletable)
                            return (false, "That troop cannot be removed.");
                        troop.Remove();
                        return (true, null);
                    }
                    case JoinCap:
                        AutoJoinBehavior.SetJoinCap(OwnTroop(hero, a, 0), Math.Max(0, Int(a, 1)));
                        return (true, null);
                    case EnsureTroops:
                    {
                        var faction = WFaction.Get(
                            (IFaction)Clan.All.FirstOrDefault(c => c.StringId == Arg(a, 0))
                                ?? Kingdom.All.FirstOrDefault(k => k.StringId == Arg(a, 0))
                        );
                        if (faction == null || !TroopOwners.CanEdit(hero, faction.Base))
                            return (false, "Not your clan or kingdom.");
                        TroopBuilder.EnsureTroopsExist(faction);
                        return (true, null);
                    }
                    case Appearance:
                        ApplyAppearance(OwnTroop(hero, a, 0), a);
                        return (true, null);
                    case UnlockCounts:
                    {
                        var counts = new Dictionary<ItemObject, int>();
                        for (int i = 1; i + 1 < a.Count; i += 2)
                        {
                            var item = MBObjectManager.Instance.GetObject<ItemObject>(a[i]);
                            var n = Math.Min(Int(a, i + 1), MaxReportedPerItem);
                            if (item == null || n <= 0)
                                continue;
                            counts[item] = counts.TryGetValue(item, out var c) ? c + n : n;
                        }
                        UnlocksBehavior.Instance?.AddReportedCounts(
                            counts,
                            kills: Arg(a, 0) == "kill"
                        );
                        return (true, null);
                    }
                    default:
                        return (false, $"Unknown troop edit '{op}'.");
                }
            }
            catch (UnauthorizedAccessException e)
            {
                return (false, e.Message);
            }
            catch (Exception e)
            {
                Log.Exception(e, $"Co-op: '{op}' for {hero?.Name} failed.");
                return (false, "The server could not do that: " + e.GetBaseException().Message);
            }
        }

        /// <summary>Copies only appearance fields; everything else stays the server's.</summary>
        private static void ApplyAppearance(WCharacter troop, IList<string> a)
        {
            var name = Arg(a, 1);
            if (!string.IsNullOrWhiteSpace(name))
                troop.Name = name.Trim();
            troop.IsFemale = Bool(a, 2);
            var culture = MBObjectManager.Instance.GetObject<CultureObject>(Arg(a, 3));
            if (culture != null && culture.StringId != troop.Culture?.StringId)
                troop.Culture = new WCulture(culture);
            troop.Race = Int(a, 4);
            troop.FormationClassOverride = (FormationClass)Int(a, 5);
            troop.FormationClass = troop.ComputeFormationClass();
            troop.IsMariner = Bool(a, 6);

            new TroopBodySaveData
            {
                AgeMin = Float(a, 7),
                AgeMax = Float(a, 8),
                WeightMin = Float(a, 9),
                WeightMax = Float(a, 10),
                BuildMin = Float(a, 11),
                BuildMax = Float(a, 12),
                HeightMin = Float(a, 13),
                HeightMax = Float(a, 14),
            }.Apply(troop.Body);
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ //
        //                         Helpers                        //
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ //

        private static WCharacter Troop(string id)
        {
            var troop = string.IsNullOrEmpty(id) ? null : WCharacter.FromStringId(id);
            if (troop?.Base == null)
                throw new ArgumentException("Unknown troop " + id);
            return troop;
        }

        /// <summary>The troop at a[index], which must be a custom troop of the hero's own clan or kingdom.</summary>
        private static WCharacter OwnTroop(Hero hero, IList<string> a, int index)
        {
            var troop = Troop(Arg(a, index));
            var faction = troop.IsCustom ? troop.Faction as WFaction : null;
            if (faction == null || !TroopOwners.CanEdit(hero, faction.Base))
                throw new UnauthorizedAccessException(
                    "You can only edit your own clan's or kingdom's troops."
                );
            return troop;
        }

        private static string Arg(IList<string> a, int i) => i < a.Count ? a[i] : "";

        private static int Int(IList<string> a, int i) =>
            int.Parse(Arg(a, i), NumberStyles.Integer, CultureInfo.InvariantCulture);

        private static float Float(IList<string> a, int i) =>
            float.Parse(Arg(a, i), NumberStyles.Float, CultureInfo.InvariantCulture);

        private static bool Bool(IList<string> a, int i) => Arg(a, i) == "1";
    }
}
