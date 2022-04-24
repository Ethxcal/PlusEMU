﻿using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Rooms.Engine;

internal class MoveAvatarEvent : IPacketEvent
{
    public void Parse(GameClient session, ClientPacket packet)
    {
        if (!session.GetHabbo().InRoom)
            return;
        var room = session.GetHabbo().CurrentRoom;
        if (room == null)
            return;
        var user = room.GetRoomUserManager().GetRoomUserByHabbo(session.GetHabbo().Id);
        if (user == null || !user.CanWalk)
            return;
        var moveX = packet.PopInt();
        var moveY = packet.PopInt();
        if (moveX == user.X && moveY == user.Y)
            return;
        if (user.RidingHorse)
        {
            var horse = room.GetRoomUserManager().GetRoomUserByVirtualId(user.HorseId);
            if (horse != null)
                horse.MoveTo(moveX, moveY);
        }
        user.MoveTo(moveX, moveY);
    }
}