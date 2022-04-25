﻿using System;
using System.Linq;
using Plus.Communication.Packets.Outgoing.Users;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Users;

internal class GetRelationshipsEvent : IPacketEvent
{
    public void Parse(GameClient session, ClientPacket packet)
    {
        var habbo = PlusEnvironment.GetHabboById(packet.PopInt());
        if (habbo == null)
            return;
        habbo.Relationships = habbo.Relationships.OrderBy(x => Random.Shared.Next()).ToDictionary(item => item.Key, item => item.Value);
        session.SendPacket(new GetRelationshipsComposer(habbo));
    }
}