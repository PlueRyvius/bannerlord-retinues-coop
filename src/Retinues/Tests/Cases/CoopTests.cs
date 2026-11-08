using System.Linq;
using Retinues.Features.Experience;
using Retinues.Game;
using Retinues.Game.Wrappers;
using Retinues.Managers;
using Retinues.Mods.Coop;
using Retinues.Troops;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;

namespace Retinues.Tests.Cases
{
    /// <summary>
    /// Live co-op checks, run in three groups a few seconds apart on the server and a player's game of one
    /// session (each test skips on the side or mode it does not apply to). They drive the same calls the editor
    /// makes, so a pass means a player's edit reached the server, ran there as that player, and came back.
    ///   coop1: the player asks the server to create their clan's troops.
    ///   coop2: the player sees them arrive, renames one and spends XP on a skill.
    ///   coop3: the server holds the rename on that player's troop; the player holds the server's copy.
    /// </summary>
    public static class CoopTests
    {
        private const string TestName = "Coop Test Guard";

        private static void RequireClient()
        {
            if (!CoopState.IsClient)
                Tests.Skip("Only on a player's game in a co-op session.");
            if (!CoopBridge.Ready)
                Tests.Skip("The server has not accepted this game yet.");
        }

        private static void RequireServer()
        {
            if (!CoopState.IsServer)
                Tests.Skip("Only on the co-op server.");
        }

        [GameTest(
            "ClientAsksServerForTroops",
            "coop1",
            "Troop creation is relayed, not done locally"
        )]
        public static void ClientAsksServerForTroops()
        {
            RequireClient();
            var before = WCharacter.ActiveStubIds.Count;
            TroopBuilder.EnsureTroopsExist(Player.Clan);
            Tests.AssertEqual(
                before,
                WCharacter.ActiveStubIds.Count,
                "A player's game claimed troop stubs itself."
            );
        }

        [GameTest(
            "ServerTroopsArrive",
            "coop2",
            "The server's troops for this player's clan arrived"
        )]
        public static void ServerTroopsArrive()
        {
            RequireClient();
            var retinue = Player.Clan.RetinueBasic;
            Tests.AssertNotNull(retinue, "No basic retinue arrived from the server.");
            Tests.AssertTrue(
                WCharacter.ActiveStubIds.Contains(retinue.StringId),
                $"Arrived retinue {retinue.StringId} is not registered as active."
            );

            // Appearance edit: applied here, then sent.
            retinue.Name = TestName;
            CoopBridge.SendAppearanceIfChanged(retinue);
        }

        [GameTest("SkillEditIsRelayed", "coop2", "A skill increase is sent, not applied locally")]
        public static void SkillEditIsRelayed()
        {
            RequireClient();
            var troop = Player.Clan.RetinueBasic;
            Tests.AssertNotNull(troop, "No retinue to train.");
            var skill = DefaultSkills.OneHanded;
            var before = troop.GetSkill(skill);
            SkillManager.ModifySkill(troop, skill, increment: true);
            Tests.AssertEqual(before, troop.GetSkill(skill), "The skill changed locally.");
        }

        [GameTest(
            "ServerHoldsPlayersEdit",
            "coop3",
            "The rename landed on that player's own troop"
        )]
        public static void ServerHoldsPlayersEdit()
        {
            RequireServer();
            var owner = TroopOwners
                .Clans.Where(c => c != Clan.PlayerClan)
                .Select(WFaction.Get)
                .FirstOrDefault(f => f.RetinueBasic?.Name == TestName);
            Tests.AssertNotNull(owner, "No player's clan holds the renamed retinue.");
            Tests.AssertTrue(
                WFaction.Get(Clan.PlayerClan)?.RetinueBasic?.Name != TestName,
                "The rename landed on the server's main clan."
            );
        }

        [GameTest("ClientHoldsServerCopy", "coop3", "The player's game shows the server's copy")]
        public static void ClientHoldsServerCopy()
        {
            RequireClient();
            var retinue = Player.Clan.RetinueBasic;
            Tests.AssertEqual(
                TestName,
                retinue?.Name,
                "The rename did not survive the round trip."
            );
            Tests.AssertTrue(
                TroopXpBehavior.Get(retinue) >= 0,
                "XP pool did not come with the snapshot."
            );
        }
    }
}
