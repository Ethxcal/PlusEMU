﻿using System;
using Plus.Core.Settings;
using Plus.Database;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.Groups;

internal class DeleteGroupEvent : IPacketEvent
{
    private readonly IGroupManager _groupManager;
    private readonly IDatabase _database;
    private readonly IRoomManager _roomManager;
    private readonly ISettingsManager _settingsManager;

    public DeleteGroupEvent(IGroupManager groupManager, IDatabase database, IRoomManager roomManager, ISettingsManager settingsManager)
    {
        _groupManager = groupManager;
        _database = database;
        _roomManager = roomManager;
        _settingsManager = settingsManager;
    }
    public void Parse(GameClient session, ClientPacket packet)
    {
        if (!_groupManager.TryGetGroup(packet.PopInt(), out var group))
        {
            session.SendNotification("Oops, we couldn't find that group!");
            return;
        }
        if (group.CreatorId != session.GetHabbo().Id && !session.GetHabbo().GetPermissions().HasRight("group_delete_override")) //Maybe a FUSE check for staff override?
        {
            session.SendNotification("Oops, only the group owner can delete a group!");
            return;
        }
        if (group.MemberCount >= Convert.ToInt32(_settingsManager.TryGetValue("group.delete.member.limit")) &&
            !session.GetHabbo().GetPermissions().HasRight("group_delete_limit_override"))
        {
            session.SendNotification("Oops, your group exceeds the maximum amount of members (" + Convert.ToInt32(_settingsManager.TryGetValue("group.delete.member.limit")) +
                                     ") a group can exceed before being eligible for deletion. Seek assistance from a staff member.");
            return;
        }
        if (!_roomManager.TryGetRoom(group.RoomId, out var room))
            return;
        if (!RoomFactory.TryGetData(group.RoomId, out var _))
            return;
        room.Group = null;

        //Remove it from the cache.
        _groupManager.DeleteGroup(group.Id);

        //Now the :S stuff.
        using (var dbClient = _database.GetQueryReactor())
        {
            dbClient.RunQuery("DELETE FROM `groups` WHERE `id` = '" + group.Id + "'");
            dbClient.RunQuery("DELETE FROM `group_memberships` WHERE `group_id` = '" + group.Id + "'");
            dbClient.RunQuery("DELETE FROM `group_requests` WHERE `group_id` = '" + group.Id + "'");
            dbClient.RunQuery("UPDATE `rooms` SET `group_id` = '0' WHERE `group_id` = '" + group.Id + "' LIMIT 1");
            dbClient.RunQuery("UPDATE `user_stats` SET `groupid` = '0' WHERE `groupid` = '" + group.Id + "' LIMIT 1");
            dbClient.RunQuery("DELETE FROM `items_groups` WHERE `group_id` = '" + group.Id + "'");
        }

        //Unload it last.
        _roomManager.UnloadRoom(room.Id);

        //Say hey!
        session.SendNotification("You have successfully deleted your group.");
    }
}