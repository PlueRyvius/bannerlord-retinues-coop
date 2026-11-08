using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Retinues.Game.Wrappers;
using Retinues.GUI.Editor;
using Retinues.Utils;

namespace Retinues.Mods.Coop
{
    /// <summary>
    /// Starts Retinues' Bannerlord Coop support. The network part lives in Retinues.Coop.dll next to Retinues.dll:
    /// it references Coop's assemblies, so it is loaded by hand, and only when Coop is in the game. Without Coop
    /// (single player) nothing here does anything.
    /// </summary>
    public static class CoopModule
    {
        private const string AdapterFile = "Retinues.Coop.dll";
        private const string SessionType = "Retinues.Coop.Handlers.Session";

        private static bool _setUp;
        private static bool _adapterTried;
        private static Action _sessionTick; // the adapter's Session.Tick: arms its handler per session

        /// <summary>
        /// Main menu, or the first tick: Coop 0.1.7's dedicated server never sets an initial screen.
        /// </summary>
        public static void Setup()
        {
            if (_setUp)
                return;
            _setUp = true;
            CoopState.ModulesLoaded();

            // The dedicated server never registers with MCM, so its options would all read 0/false.
            if (CoopState.IsServerProcess)
                Configuration.Config.EnsureLoadedWithoutMcm();
            CoopBridge.TroopsChanged += RefreshEditor;
            TryLoadAdapter();
        }

        /// <summary>Every application tick.</summary>
        public static void Tick()
        {
            if (!_setUp)
                Setup();
            if (!_adapterTried)
                TryLoadAdapter();
            if (!CoopState.Loaded)
                return;

            CoopState.Tick();

            if (_sessionTick != null)
            {
                try
                {
                    _sessionTick();
                }
                catch (Exception e)
                {
                    _sessionTick = null;
                    Log.Warn(
                        "Co-op adapter stopped; troop edits and troops stay local: "
                            + e.GetBaseException().Message
                    );
                }
            }

            try
            {
                CoopBridge.SnapshotTick();
            }
            catch (Exception e)
            {
                Log.Warn("Co-op: sending troops failed: " + e.GetBaseException().Message);
            }
        }

        private static void TryLoadAdapter()
        {
            if (_adapterTried)
                return;
            if (!CoopState.Loaded)
            {
                if (CoopState.Resolved)
                    _adapterTried = true; // No Coop in this game: nothing to load.
                return;
            }

            _adapterTried = true;
            try
            {
                var path = Path.Combine(
                    Path.GetDirectoryName(typeof(CoopModule).Assembly.Location) ?? ".",
                    AdapterFile
                );
                if (!File.Exists(path))
                {
                    Log.Warn(
                        AdapterFile + " is missing next to Retinues.dll; co-op support is off."
                    );
                    return;
                }

                var adapter = Assembly.LoadFrom(path);
                var types = adapter.GetTypes().Length; // load the types now, inside this try
                var tick = adapter
                    .GetType(SessionType, true)
                    .GetMethod("Tick", BindingFlags.Public | BindingFlags.Static);
                _sessionTick = (Action)Delegate.CreateDelegate(typeof(Action), tick);
                Log.Info(
                    $"Co-op adapter loaded ({types} types, protocol {CoopBridge.ProtocolVersion}); it arms when a session starts."
                );
            }
            catch (Exception e)
            {
                Log.Warn(
                    "Co-op adapter did not load; co-op support is off: "
                        + e.GetBaseException().Message
                );
            }
        }

        /// <summary>A snapshot replaced a faction's troops: refresh the editor if it shows that faction.</summary>
        private static void RefreshEditor(WFaction faction)
        {
            try
            {
                if (ClanScreen.Instance == null || State.Faction is not WFaction shown)
                    return;
                if (shown != faction)
                    return;

                var selected = State.Troop?.StringId;
                State.UpdateFaction(shown);
                var again = shown.Troops.FirstOrDefault(t => t?.StringId == selected);
                if (again != null)
                    State.UpdateTroop(again);
            }
            catch (Exception e)
            {
                Log.Exception(e, "Co-op: editor refresh failed.");
            }
        }
    }
}
