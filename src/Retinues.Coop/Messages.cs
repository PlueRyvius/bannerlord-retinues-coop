using System.Collections.Generic;
using Common.Messaging;
using ProtoBuf;

namespace Retinues.Coop.Messages
{
    // Retinues' own co-op messages, in its own namespace. WIRE-FORMAT NOTE: Coop derives each message's wire id
    // from a hash of the type's full name, so these names are part of the protocol. Never rename or move them; add
    // members with new tags, and raise CoopBridge.ProtocolVersion when an older copy would misread them. Every
    // message carries that version and a side that speaks another drops it.

    /// <summary>Player -> server, once the joined campaign is ready: "I speak protocol N; may I relay?"</summary>
    [ProtoContract(SkipConstructor = true)]
    public sealed class RtHello : ICommand
    {
        [ProtoMember(1)]
        public int ProtocolVersion { get; set; }
    }

    /// <summary>Server -> that player: accepted or not, and the server's Retinues settings.</summary>
    [ProtoContract(SkipConstructor = true)]
    public sealed class RtHelloReply : IEvent
    {
        [ProtoMember(1)]
        public int ProtocolVersion { get; set; }

        [ProtoMember(2)]
        public bool Accepted { get; set; }

        [ProtoMember(3)]
        public string Detail { get; set; } = "";

        [ProtoMember(4)]
        public List<string> Settings { get; set; }
    }

    /// <summary>Player -> server: a troop edit for the server to carry out as this player.</summary>
    [ProtoContract(SkipConstructor = true)]
    public sealed class RtAction : ICommand
    {
        [ProtoMember(1)]
        public string Op { get; set; } = "";

        [ProtoMember(2)]
        public List<string> Args { get; set; }

        [ProtoMember(3)]
        public int Sequence { get; set; }

        [ProtoMember(4)]
        public int ProtocolVersion { get; set; }
    }

    /// <summary>Server -> the sending player: whether the edit worked, with a line when it did not.</summary>
    [ProtoContract(SkipConstructor = true)]
    public sealed class RtActionResult : IEvent
    {
        [ProtoMember(1)]
        public string Op { get; set; } = "";

        [ProtoMember(2)]
        public int Sequence { get; set; }

        [ProtoMember(3)]
        public bool Ok { get; set; }

        [ProtoMember(4)]
        public string Message { get; set; } = "";

        [ProtoMember(5)]
        public int ProtocolVersion { get; set; }
    }

    /// <summary>Server -> accepted players: one faction's custom troops, as Retinues saves them.</summary>
    [ProtoContract(SkipConstructor = true)]
    public sealed class RtSnapshot : IEvent
    {
        [ProtoMember(1)]
        public string FactionId { get; set; } = "";

        [ProtoMember(2)]
        public string Payload { get; set; } = "";

        [ProtoMember(3)]
        public int ProtocolVersion { get; set; }
    }

    /// <summary>Player -> server: send every troop-owning faction's troops (after the hello).</summary>
    [ProtoContract(SkipConstructor = true)]
    public sealed class RtSnapshotRequest : ICommand
    {
        [ProtoMember(1)]
        public int ProtocolVersion { get; set; }
    }
}
