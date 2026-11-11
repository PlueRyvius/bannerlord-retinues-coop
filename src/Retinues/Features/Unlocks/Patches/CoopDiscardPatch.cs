using HarmonyLib;
using Retinues.Mods.Coop;
using Retinues.Utils;
using TaleWorlds.CampaignSystem.Inventory;
using TaleWorlds.CampaignSystem.Roster;

namespace Retinues.Features.Unlocks.Patches
{
    /// <summary>
    /// Co-op: Bannerlord Coop replaces the inventory screen's Done on a player's game and replays
    /// the trade on the server, so the game's "items discarded" event fires on neither side and
    /// discards never counted toward unlocks. On a player's game this reads the discarded items
    /// the way the game's own Done does (the left roster, when the screen counts discards) and
    /// reports them to the server. Runs first, before Coop's own Done prefix takes over.
    /// </summary>
    [HarmonyPatch(typeof(InventoryLogic), nameof(InventoryLogic.DoneLogic))]
    internal static class CoopDiscardPatch
    {
        private static readonly AccessTools.FieldRef<InventoryLogic, ItemRoster[]> Rosters =
            AccessTools.FieldRefAccess<InventoryLogic, ItemRoster[]>("_rosters");

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [SafeMethod]
        private static void Prefix(InventoryLogic __instance)
        {
            if (!CoopState.IsClient || __instance?.CanGainXpFromDiscarding != true)
                return;

            var discarded = Rosters(__instance)?[0];
            if (discarded == null || discarded.Count == 0)
                return;

            UnlocksBehavior.Instance?.CountDiscards(discarded);
        }
    }
}
