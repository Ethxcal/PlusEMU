﻿using Plus.Communication.Packets.Outgoing.Messenger;
using Plus.Communication.Packets.Outgoing.Rooms.Session;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Messenger;

internal class FollowFriendEvent : IPacketEvent
{
    private readonly IGameClientManager _clientManager;

    public FollowFriendEvent(IGameClientManager clientManager)
    {
        _clientManager = clientManager;
    }

    public void Parse(GameClient session, ClientPacket packet)
    {
        var buddyId = packet.PopInt();
        if (buddyId == 0 || buddyId == session.GetHabbo().Id)
            return;
        var client = _clientManager.GetClientByUserId(buddyId);
        if (client == null || client.GetHabbo() == null)
            return;
        if (!client.GetHabbo().InRoom)
        {
            session.SendPacket(new FollowFriendFailedComposer(2));
            session.GetHabbo().GetMessenger().UpdateFriend(client.GetHabbo().Id, client, true);
            return;
        }
        if (session.GetHabbo().CurrentRoom != null && client.GetHabbo().CurrentRoom != null)
        {
            if (session.GetHabbo().CurrentRoom.RoomId == client.GetHabbo().CurrentRoom.RoomId)
                return;
        }
        session.SendPacket(new RoomForwardComposer(client.GetHabbo().CurrentRoomId));
    }
}