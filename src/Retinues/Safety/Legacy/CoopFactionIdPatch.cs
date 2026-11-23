using HarmonyLib;
using Retinues.Troops.Save;
using Retinues.Utils;
using TaleWorlds.SaveSystem.Load;

namespace Retinues.Safety.Legacy
{
    /// <summary>
    /// Coop fork only: saves from the fork's releases coop.1 to coop.3 stored
    /// FactionSaveData.FactionId as field 18, which Retinues 1.5.14.35 gave to
    /// VeteranCaravanGuard; FactionId is now field 100. The game's save loader skips a field whose
    /// stored type does not match (a string where a troop record is now) and has no conversion
    /// hook, so after each FactionSaveData is filled this reads that string back and keeps it as
    /// FactionId. Without it, every player clan's and kingdom's troops in such a save would lose
    /// the faction they belong to. Runs once per loaded object; for any other object it is a single
    /// type check.
    /// </summary>
    [HarmonyPatch(typeof(ObjectLoadData), nameof(ObjectLoadData.FillObject))]
    internal static class CoopFactionIdPatch
    {
        private const int LegacyFactionIdField = 18;

        [HarmonyPostfix]
        [SafeMethod]
        private static void Postfix(ObjectLoadData __instance)
        {
            if (__instance.Target is not FactionSaveData data || data.FactionId != null)
                return;
            if (__instance.GetFieldValueBySaveId(LegacyFactionIdField) is string factionId)
            {
                data.FactionId = factionId;
                Log.Info($"Read faction '{factionId}' from a Coop fork save (old field 18).");
            }
        }
    }
}
