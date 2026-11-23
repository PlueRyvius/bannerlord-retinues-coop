using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Retinues.Game.Events;
using Retinues.Game.Wrappers;
using Retinues.GUI.Helpers;
using Retinues.Mods.Coop;
using Retinues.Utils;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Localization;

namespace Retinues.Features.Unlocks.Patches
{
    /// <summary>
    /// Adds a special capture option for kingdom rulers:
    /// force them to reveal the secrets of their vassal reward items.
    /// </summary>
    [HarmonyPatch(typeof(LordConversationsCampaignBehavior))]
    public static class VassalRewardSecretsPatch
    {
        private const string PlayerLineId = "lord_defeat_vassal_secrets";
        private const string PlayerLineToken = "defeated_lord_answer";
        private const string PlayerLineOutput = "lord_defeat_vassal_secrets_ruler_answer";
        private const string RulerLineId = "lord_defeat_vassal_secrets_ruler_answer_line";
        private const string RulerLineInput = PlayerLineOutput;
        private const string RulerLineOutput = "close_window";

        [HarmonyPatch("AddLordLiberateConversations")]
        [HarmonyPostfix]
        private static void AddSecretOption(CampaignGameStarter starter)
        {
            if (starter == null)
                return;

            // Player option
            starter.AddPlayerLine(
                PlayerLineId,
                PlayerLineToken,
                PlayerLineOutput,
                L.S(
                    "vassal_secrets_option",
                    "Reveal the secrets of your most precious artifacts and I will let you go."
                ),
                new ConversationSentence.OnConditionDelegate(Condition),
                new ConversationSentence.OnConsequenceDelegate(Consequence),
                100,
                null
            );

            // Ruler answer line (fires after the player picks the option)
            starter.AddDialogLine(
                RulerLineId,
                RulerLineInput,
                RulerLineOutput,
                L.S(
                    "vassal_secrets_ruler_answer",
                    "Very well... I will share the knowledge of these relics, if it buys my freedom."
                ),
                null,
                null,
                100,
                null
            );
        }

        /// <summary>
        /// Only show the option when:
        /// - Conversation is a captured lord after battle.
        /// - The hero is a kingdom ruler.
        /// - Their culture has at least one vassal reward item that is not yet unlocked.
        /// </summary>
        private static bool Condition()
        {
            // Must be the captured-lord post-battle context.
            if (Campaign.Current?.CurrentConversationContext != ConversationContext.CapturedLord)
                return false;

            Hero ruler = Hero.OneToOneConversationHero;
            if (ruler == null)
                return false;

            if (ruler.MapFaction is not Kingdom kingdom)
                return false;

            if (kingdom.Leader != ruler)
                return false; // not the ruler, just a regular lord

            var culture = kingdom.Culture;
            if (
                culture == null
                || culture.VassalRewardItems == null
                || culture.VassalRewardItems.Count == 0
            )
                return false;

            var battle = new Battle();
            if (battle.AllyLeaders.Count() > 0)
                return false; // must have won the battle by themselves

            Log.Info(battle.TotalTroopCount.ToString());

            // Only offer if at least one reward item exists that is not yet unlocked.
            bool anyLocked = culture
                .VassalRewardItems.Where(i => i != null)
                .Select(i => new WItem(i))
                .Any(wi => !wi.IsUnlocked && wi.IsVassalRewardItem);

            return anyLocked;
        }

        /// <summary>
        /// Apply capture penalty (like taking prisoner), then release,
        /// and unlock all vassal reward items for that ruler's culture.
        /// </summary>
        private static void Consequence()
        {
            Hero ruler = Hero.OneToOneConversationHero;
            if (ruler == null)
                return;

            // Co-op, a player's game: only the server may take a lord prisoner or unlock items, so it does both.
            // The answer it sends back says what was unlocked.
            if (CoopState.IsClient)
            {
                Campaign.Current.CurrentConversationContext = ConversationContext.Default;
                CoopBridge.Relay(CoopActions.RulerSecrets, ruler.StringId);
                return;
            }

            var unlockedItems = RevealSecrets(ruler, PartyBase.MainParty, requireLocked: false);
            if (unlockedItems == null || unlockedItems.Count == 0)
                return;

            // Play sound when the popup appears
            Sound.Play2D(Sound.Education);

            // Show popup
            Notifications.Popup(
                L.T("vassal_secrets_title", "Secrets Unlocked"),
                new TextObject(UnlockedText(ruler, unlockedItems))
            );
        }

        /// <summary>"You have unlocked the secrets of: ...", for the items a ruler revealed.</summary>
        public static string UnlockedText(Hero ruler, List<WItem> unlockedItems) =>
            L.T("vassal_secrets_unlocked_body", "You have unlocked the secrets of: {ITEMS}.")
                .SetTextVariable("CULTURE", (ruler?.MapFaction as Kingdom)?.Culture?.Name)
                .SetTextVariable("ITEMS", string.Join(", ", unlockedItems.Select(i => i.Name)))
                .ToString();

        /// <summary>
        /// Unlocks the vassal reward items of a kingdom ruler's culture, then has the captor take the ruler
        /// prisoner and release them at once, for the same relation penalty and events as a capture. Returns the
        /// items unlocked, or null when the hero is not a ruler with such items (or, with requireLocked, when
        /// none is still locked); then nothing happens.
        /// </summary>
        public static List<WItem> RevealSecrets(Hero ruler, PartyBase captor, bool requireLocked)
        {
            if (ruler?.IsAlive != true || ruler.IsPrisoner)
                return null;

            if (ruler.MapFaction is not Kingdom kingdom || kingdom.Leader != ruler)
                return null;

            var culture = kingdom.Culture;
            if (
                culture == null
                || culture.VassalRewardItems == null
                || culture.VassalRewardItems.Count == 0
            )
                return null;

            if (
                requireLocked
                && !culture
                    .VassalRewardItems.Where(i => i != null)
                    .Select(i => new WItem(i))
                    .Any(wi => !wi.IsUnlocked && wi.IsVassalRewardItem)
            )
                return null;

            List<WItem> unlockedItems = [];

            // 1) Unlock all relevant vassal reward items for this culture.
            try
            {
                foreach (var item in culture.VassalRewardItems)
                {
                    if (item == null)
                        continue;

                    var wItem = new WItem(item);
                    if (wItem.IsVassalRewardItem && !wItem.IsUnlocked)
                    {
                        wItem.Unlock();
                        unlockedItems.Add(wItem);
                        Log.Info(
                            $"VassalRewardSecrets: Unlocked vassal reward item '{wItem.StringId}' for culture '{culture.StringId}'."
                        );
                    }
                }
            }
            catch (System.Exception e)
            {
                Log.Exception(e);
            }

            // 2) Simulate capture to trigger the same relation penalty and events.
            try
            {
                // Make sure we are in a sane context.
                if (Campaign.Current.CurrentConversationContext == ConversationContext.CapturedLord)
                    Campaign.Current.CurrentConversationContext = ConversationContext.Default;

                // Take prisoner -> fires OnPrisonerTaken and related penalties.
                TakePrisonerAction.Apply(captor, ruler);

                // 3) Immediately release them as if let go after battle.
                EndCaptivityAction.ApplyByReleasedAfterBattle(ruler);
            }
            catch (System.Exception e)
            {
                Log.Exception(e);
            }

            return unlockedItems;
        }
    }
}
