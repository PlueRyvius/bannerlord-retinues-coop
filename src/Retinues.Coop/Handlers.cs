using System;
using System.Collections.Generic;
using System.Linq;
using Autofac;
using Common;
using Common.Messaging;
using Common.Network;
using GameInterface;
using GameInterface.Services.GameState.Messages;
using GameInterface.Services.ObjectManager;
using GameInterface.Services.Players;
using LiteNetLib;
using Retinues.Coop.Messages;
using Retinues.Mods.Coop;
using Retinues.Utils;
using TaleWorlds.CampaignSystem;

// The handlers live in Retinues' own namespace, so Coop doesn't find them on its own. Session.Tick arms the one for
// this side whenever Coop's session container appears or changes, and drops it when the session ends.
namespace Retinues.Coop.Handlers
{
    /// <summary>Arms this side's handler for each Coop session. Called every application tick by Retinues.</summary>
    public static class Session
    {
        private static ILifetimeScope armedScope;
        private static IDisposable handler;

        public static void Tick()
        {
            ContainerProvider.TryGetContainer(out var scope);
            if (ReferenceEquals(scope, armedScope))
                return;
            Drop();
            if (scope == null)
                return;
            armedScope = scope;
            if (
                !ContainerProvider.TryResolve<IMessageBroker>(out var broker)
                || !ContainerProvider.TryResolve<INetwork>(out var network)
                || broker == null
                || network == null
            )
            {
                Log.Warn(
                    "Co-op: Coop's session has no message broker or network; troops stay local."
                );
                return;
            }
            handler = CoopState.IsServerProcess
                ? new RtServerHandler(broker, network)
                : new RtClientHandler(broker, network);
        }

        private static void Drop()
        {
            armedScope = null;
            var old = handler;
            handler = null;
            try
            {
                old?.Dispose();
            }
            catch (Exception e)
            {
                Log.Warn("Co-op: handler did not close cleanly: " + e.GetBaseException().Message);
            }
        }
    }

    /// <summary>Player's game: says hello; once accepted, relays edits and loads the server's troops.</summary>
    public sealed class RtClientHandler : IDisposable
    {
        private readonly IMessageBroker broker;
        private readonly INetwork network;
        private int sequence;
        private bool helloSent;

        public RtClientHandler(IMessageBroker broker, INetwork network)
        {
            this.broker = broker;
            this.network = network;
            CoopBridge.SessionEnded(); // a fresh session starts from nothing
            CoopBridge.ApplyForcedSettings();
            broker.Subscribe<CampaignReady>(HandleCampaignReady);
            broker.Subscribe<RtHelloReply>(HandleHelloReply);
            broker.Subscribe<RtActionResult>(HandleActionResult);
            broker.Subscribe<RtSnapshot>(HandleSnapshot);
            // Armed after the campaign was already ready: CampaignReady has gone by, so say hello now.
            if (Campaign.Current != null)
                SayHello();
        }

        public void Dispose()
        {
            broker.Unsubscribe<CampaignReady>(HandleCampaignReady);
            broker.Unsubscribe<RtHelloReply>(HandleHelloReply);
            broker.Unsubscribe<RtActionResult>(HandleActionResult);
            broker.Unsubscribe<RtSnapshot>(HandleSnapshot);
            CoopBridge.SessionEnded();
        }

        private void HandleCampaignReady(MessagePayload<CampaignReady> payload) => SayHello();

        private void SayHello()
        {
            if (helloSent)
                return;
            helloSent = true;
            network.SendAll(new RtHello { ProtocolVersion = CoopBridge.ProtocolVersion });
        }

        private void HandleHelloReply(MessagePayload<RtHelloReply> payload)
        {
            var reply = payload.What;
            if (!reply.Accepted || reply.ProtocolVersion != CoopBridge.ProtocolVersion)
            {
                Log.Warn(
                    $"Co-op: the server did not accept this game ({reply.Detail}; server protocol {reply.ProtocolVersion}, here {CoopBridge.ProtocolVersion}). Troop edits are off."
                );
                return;
            }

            GameThread.RunSafe(
                () =>
                {
                    CoopBridge.ApplyServerSettings(reply.Settings ?? []);
                    CoopBridge.SendAction = (op, args) =>
                        network.SendAll(
                            new RtAction
                            {
                                Op = op,
                                Args = [.. args],
                                Sequence = ++sequence,
                                ProtocolVersion = CoopBridge.ProtocolVersion,
                            }
                        );
                    CoopBridge.Ready = true;
                    Log.Info(
                        $"Co-op: the server accepted protocol {CoopBridge.ProtocolVersion}; troops follow the server."
                    );
                    network.SendAll(
                        new RtSnapshotRequest { ProtocolVersion = CoopBridge.ProtocolVersion }
                    );
                },
                false,
                "Retinues co-op hello"
            );
        }

        private void HandleActionResult(MessagePayload<RtActionResult> payload)
        {
            var r = payload.What;
            if (r.ProtocolVersion != CoopBridge.ProtocolVersion)
                return;
            GameThread.RunSafe(
                () => CoopBridge.ShowResult(r.Ok, r.Message),
                false,
                "Retinues co-op result"
            );
        }

        private void HandleSnapshot(MessagePayload<RtSnapshot> payload)
        {
            var m = payload.What;
            if (m.ProtocolVersion != CoopBridge.ProtocolVersion || !CoopBridge.Ready)
                return;
            GameThread.RunSafe(
                () => CoopBridge.ApplySnapshot(m.FactionId, m.Payload),
                false,
                "Retinues co-op troops"
            );
        }
    }

    /// <summary>
    /// Server: accepts players whose protocol matches, carries out their edits as them, and sends troops to the
    /// accepted ones. A player who isn't accepted gets nothing and has nothing run for them.
    /// </summary>
    public sealed class RtServerHandler : IDisposable
    {
        private readonly IMessageBroker broker;
        private readonly INetwork network;
        private readonly HashSet<NetPeer> accepted = [];
        private readonly Dictionary<NetPeer, Queue<DateTime>> recent = [];

        /// <summary>A backstop against a flood: the editor never sends more than a few edits a second.</summary>
        private const int ActionsPerSecond = 20;

        public RtServerHandler(IMessageBroker broker, INetwork network)
        {
            this.broker = broker;
            this.network = network;
            CoopBridge.SessionEnded();
            CoopBridge.ApplyForcedSettings();
            broker.Subscribe<RtHello>(HandleHello);
            broker.Subscribe<RtAction>(HandleAction);
            broker.Subscribe<RtSnapshotRequest>(HandleSnapshotRequest);
            CoopBridge.Broadcast = (factionId, data) =>
            {
                List<NetPeer> peers;
                lock (accepted)
                    peers = [.. accepted];
                foreach (var peer in peers)
                    network.Send(
                        peer,
                        new RtSnapshot
                        {
                            FactionId = factionId,
                            Payload = data,
                            ProtocolVersion = CoopBridge.ProtocolVersion,
                        }
                    );
            };
        }

        public void Dispose()
        {
            broker.Unsubscribe<RtHello>(HandleHello);
            broker.Unsubscribe<RtAction>(HandleAction);
            broker.Unsubscribe<RtSnapshotRequest>(HandleSnapshotRequest);
            CoopBridge.SessionEnded();
        }

        private void HandleHello(MessagePayload<RtHello> payload)
        {
            if (payload.Who is not NetPeer peer)
                return;
            var ok = payload.What.ProtocolVersion == CoopBridge.ProtocolVersion;
            var detail = ok
                ? "accepted"
                : $"protocol {payload.What.ProtocolVersion} is not this server's {CoopBridge.ProtocolVersion}; update Retinues";
            lock (accepted)
            {
                if (ok)
                    accepted.Add(peer);
                else
                    accepted.Remove(peer);
            }

            GameThread.RunSafe(
                () =>
                    network.Send(
                        peer,
                        new RtHelloReply
                        {
                            ProtocolVersion = CoopBridge.ProtocolVersion,
                            Accepted = ok,
                            Detail = detail,
                            Settings = ok ? CoopBridge.SettingsForJoin() : null,
                        }
                    ),
                false,
                "Retinues co-op hello reply"
            );
            Log.Info("Co-op: a player said hello: " + detail);
        }

        private bool IsAccepted(NetPeer peer, int version)
        {
            if (version != CoopBridge.ProtocolVersion)
                return false;
            lock (accepted)
                return accepted.Contains(peer);
        }

        private void HandleSnapshotRequest(MessagePayload<RtSnapshotRequest> payload)
        {
            if (payload.Who is not NetPeer peer || !IsAccepted(peer, payload.What.ProtocolVersion))
                return;
            GameThread.RunSafe(
                () =>
                {
                    var all = CoopBridge.SnapshotsForJoin();
                    foreach (var (factionId, data) in all)
                        network.Send(
                            peer,
                            new RtSnapshot
                            {
                                FactionId = factionId,
                                Payload = data,
                                ProtocolVersion = CoopBridge.ProtocolVersion,
                            }
                        );
                    Log.Info($"Co-op: sent {all.Count} faction(s) of troops to a joining player.");
                },
                false,
                "Retinues co-op snapshot request"
            );
        }

        private void HandleAction(MessagePayload<RtAction> payload)
        {
            if (payload.Who is not NetPeer peer)
                return;
            var msg = payload.What;

            void Reply(bool ok, string message) =>
                network.Send(
                    peer,
                    new RtActionResult
                    {
                        Op = msg.Op,
                        Sequence = msg.Sequence,
                        Ok = ok,
                        Message = message ?? "",
                        ProtocolVersion = CoopBridge.ProtocolVersion,
                    }
                );

            if (!IsAccepted(peer, msg.ProtocolVersion))
                return;
            if (!RateOk(peer))
            {
                Reply(false, "Too many troop edits at once; slow down a moment.");
                return;
            }
            if (
                !ContainerProvider.TryResolve<IPlayerManager>(out var players)
                || !players.TryGetPlayer(peer, out var player)
                || player is null
            )
            {
                Reply(false, "The server does not know you yet; try again in a moment.");
                return;
            }

            GameThread.RunSafe(
                () =>
                {
                    try
                    {
                        if (
                            !ContainerProvider.TryResolve<IObjectManager>(out var objects)
                            || !objects.TryGetObject<Hero>(player.HeroId, out var hero)
                            || hero is null
                        )
                        {
                            Reply(false, "Your hero was not found on the server.");
                            return;
                        }
                        var (ok, message) = CoopBridge.RunAction(hero, msg.Op, msg.Args ?? []);
                        Reply(ok, message);
                    }
                    catch (Exception e)
                    {
                        Reply(
                            false,
                            "The server could not do that: " + e.GetBaseException().Message
                        );
                    }
                },
                false,
                "Retinues co-op edit"
            );
        }

        private bool RateOk(NetPeer peer)
        {
            lock (recent)
            {
                if (!recent.TryGetValue(peer, out var q))
                    recent[peer] = q = new Queue<DateTime>();
                var now = DateTime.UtcNow;
                while (q.Count > 0 && (now - q.Peek()).TotalSeconds > 1)
                    q.Dequeue();
                if (q.Count >= ActionsPerSecond)
                    return false;
                q.Enqueue(now);
                return true;
            }
        }
    }
}
