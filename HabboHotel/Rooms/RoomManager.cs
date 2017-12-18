﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

using Plus.Core;
using Plus.HabboHotel.GameClients;
using System.Collections.Concurrent;
using Plus.Database.Interfaces;
using log4net;

namespace Plus.HabboHotel.Rooms
{
    public class RoomManager
    {
        private static readonly ILog log = LogManager.GetLogger("Plus.HabboHotel.Rooms.RoomManager");

        private readonly object _roomLoadingSync;

        private Dictionary<string, RoomModel> _roomModels;

        private ConcurrentDictionary<int, Room> _rooms;

        private DateTime _cycleLastExecution;


        public RoomManager()
        {
            this._roomModels = new Dictionary<string, RoomModel>();
            this._rooms = new ConcurrentDictionary<int, Room>();
            this._roomLoadingSync = new object();

            this.LoadModels();
        }

        public void OnCycle()
        {
            try
            {
                TimeSpan sinceLastTime = DateTime.Now - _cycleLastExecution;
                if (sinceLastTime.TotalMilliseconds >= 500)
                {
                    _cycleLastExecution = DateTime.Now;
                    foreach (Room Room in this._rooms.Values.ToList())
                    {
                        if (Room.isCrashed)
                            continue;

                        if (Room.ProcessTask == null || Room.ProcessTask.IsCompleted)
                        {
                            Room.ProcessTask = new Task(Room.ProcessRoom);
                            Room.ProcessTask.Start();
                            Room.IsLagging = 0;
                        }
                        else
                        {
                            Room.IsLagging++;
                            if (Room.IsLagging >= 30)
                            {
                                Room.isCrashed = true;
                                UnloadRoom(Room.Id);
                               // Logging.WriteLine("[RoomMgr] Room crashed (task didn't complete within 30 seconds): " + Room.RoomId);
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                ExceptionLogger.LogException(e);
            }
        }

        public int Count
        {
            get { return this._rooms.Count; }
        }

        public void LoadModels()
        {
            if (_roomModels.Count > 0)
                _roomModels.Clear();

            using (IQueryAdapter dbClient = PlusEnvironment.GetDatabaseManager().GetQueryReactor())
            {
                dbClient.SetQuery("SELECT id,door_x,door_y,door_z,door_dir,heightmap,club_only,poolmap,`wall_height` FROM `room_models` WHERE `custom` = '0'");
                DataTable Data = dbClient.GetTable();

                if (Data == null)
                    return;

                foreach (DataRow Row in Data.Rows)
                {
                    string Modelname = Convert.ToString(Row["id"]);

                    _roomModels.Add(Modelname, new RoomModel(Modelname, Convert.ToInt32(Row["door_x"]), Convert.ToInt32(Row["door_y"]), (Double)Row["door_z"], Convert.ToInt32(Row["door_dir"]),
                        Convert.ToString(Row["heightmap"]), PlusEnvironment.EnumToBool(Row["club_only"].ToString()), Convert.ToInt32(Row["wall_height"]), false));
                }
            }
        }

        public bool LoadModel(string id)
        {
            DataRow Row = null;
            using (IQueryAdapter dbClient = PlusEnvironment.GetDatabaseManager().GetQueryReactor())
            {
                dbClient.SetQuery("SELECT id,door_x,door_y,door_z,door_dir,heightmap,club_only,poolmap,`wall_height` FROM `room_models` WHERE `custom` = '1' AND `id` = @modelId LIMIT 1");
                dbClient.AddParameter("modelId", id);
                Row = dbClient.GetRow();

                if (Row == null)
                    return false;

                string Modelname = Convert.ToString(Row["id"]);

                if (!this._roomModels.ContainsKey(Modelname))
                {
                    this._roomModels.Add(Modelname, new RoomModel(Modelname, Convert.ToInt32(Row["door_x"]), Convert.ToInt32(Row["door_y"]), Convert.ToDouble(Row["door_z"]), Convert.ToInt32(Row["door_dir"]),
                      Convert.ToString(Row["heightmap"]), PlusEnvironment.EnumToBool(Row["club_only"].ToString()), Convert.ToInt32(Row["wall_height"]), true));
                }

                return true;
            }
        }

        public void ReloadModel(string Id)
        {
            if (!this._roomModels.ContainsKey(Id))
            {
                this.LoadModel(Id);
                return;
            }

            this._roomModels.Remove(Id);
            this.LoadModel(Id);
        }

        public bool TryGetModel(string id, out RoomModel model)
        {
            if (this._roomModels.ContainsKey(id))
            {
                model = this._roomModels[id];
                return true;
            }

            // Try to load this model.
            if (LoadModel(id))
            {
                RoomModel customModel = null;
                if (TryGetModel(id, out customModel))
                {
                    model = customModel;
                    return true;
                }
            }

            model = null;
            return false;
        }

        public void UnloadRoom(int roomId)
        {
            if (this._rooms.TryRemove(roomId, out Room room))
            {
                room.Dispose();
            }
        }

        public bool TryLoadRoom(int roomId, out Room instance)
        {
            Room inst = null;
            if (this._rooms.TryGetValue(roomId, out inst))
            {
                if (!inst.Unloaded)
                {
                    instance = inst;
                    return true;
                }

                instance = null;
                return false;
            }

            lock (_roomLoadingSync)
            {
                if (_rooms.TryGetValue(roomId, out inst))
                {
                    if (!inst.Unloaded)
                    {
                        instance = inst;
                        return true;
                    }

                    instance = null;
                    return false;
                }

                RoomData data = null;
                if (!RoomFactory.TryGetData(roomId, out data))
                {
                    instance = null;
                    return false;
                }

                Room myInstance = new Room(data);
                if (this._rooms.TryAdd(roomId, myInstance))
                {
                    instance = myInstance;
                    return true;
                }

                instance = null;
                return false;
            }
        }


        public List<Room> SearchGroupRooms(string query)
        {
            return this._rooms.Values.Where(x => x.Group != null && x.Group.Name.ToLower().Contains(query.ToLower()) && x.Access != RoomAccess.Invisible).OrderByDescending(x => x.UsersNow).Take(50).ToList();
        }

        public List<Room> SearchTaggedRooms(string query)
        {
            return this._rooms.Values.Where(x => x.Tags.Contains(query) && x.Access != RoomAccess.Invisible).OrderByDescending(x => x.UsersNow).Take(50).ToList();
        }

        public List<Room> GetPopularRooms(int category, int amount = 50)
        {
            return this._rooms.Values.Where(x => x.UsersNow > 0 && x.Access != RoomAccess.Invisible).OrderByDescending(x => x.UsersNow).Take(amount).ToList();
        }

        public List<Room> GetRecommendedRooms(int amount = 50, int CurrentRoomId = 0)
        {
            return this._rooms.Values.Where(x => x.Id != CurrentRoomId && x.Access != RoomAccess.Invisible).OrderByDescending(x => x.UsersNow).OrderByDescending(x => x.Score).Take(amount).ToList();
        }

        public List<Room> GetPopularRatedRooms(int amount = 50)
        {
            return this._rooms.Values.Where(x => x.Access != RoomAccess.Invisible).OrderByDescending(x => x.Score).OrderByDescending(x => x.UsersNow).Take(amount).ToList();
        }

        public List<Room> GetRoomsByCategory(int category, int amount = 50)
        {
            return this._rooms.Values.Where(x => x.Category == category && x.Access != RoomAccess.Invisible && x.UsersNow > 0).OrderByDescending(x => x.UsersNow).Take(amount).ToList();
        }

        public List<Room> GetOnGoingRoomPromotions(int Mode, int Amount = 50)
        {
            if (Mode == 17)
            {
                return this._rooms.Values.Where(x => x.HasActivePromotion && x.Access != RoomAccess.Invisible).OrderByDescending(x => x.Promotion.TimestampStarted).Take(Amount).ToList();
            }

            return this._rooms.Values.Where(x => x.HasActivePromotion && x.Access != RoomAccess.Invisible).OrderByDescending(x => x.UsersNow).Take(Amount).ToList();
        }

        public List<Room> GetPromotedRooms(int categoryId, int amount = 50)
        {
            return this._rooms.Values.Where(x => x.HasActivePromotion && x.Promotion.CategoryId == categoryId && x.Access != RoomAccess.Invisible).OrderByDescending(x => x.Promotion.TimestampStarted).Take(amount).ToList();
        }

        public List<Room> GetGroupRooms(int amount = 50)
        {
            return this._rooms.Values.Where(x => x.Group != null && x.Access != RoomAccess.Invisible).OrderByDescending(x => x.Score).Take(amount).ToList();
        }

        public List<Room> GetRoomsByIds(List<int> ids, int amount = 50)
        {
            return this._rooms.Values.Where(x => ids.Contains(x.Id) && x.Access != RoomAccess.Invisible).OrderByDescending(x => x.UsersNow).Take(amount).ToList();
        }

        public Room TryGetRandomLoadedRoom()
        {
            return this._rooms.Values.Where(x => x.UsersNow > 0 && x.Access != RoomAccess.Invisible && x.UsersNow < x.UsersMax).OrderByDescending(x => x.UsersNow).FirstOrDefault();
        }


        public bool TryGetRoom(int RoomId, out Room Room)
        {
            return this._rooms.TryGetValue(RoomId, out Room);
        }

        public RoomData CreateRoom(GameClient session, string name, string description, int category, int maxVisitors, int tradeSettings, RoomModel model, string wallpaper = "0.0", string floor = "0.0", string landscape = "0.0", int wallthick = 0, int floorthick = 0)
        {
            if (name.Length < 3)
            {
                session.SendNotification(PlusEnvironment.GetLanguageManager().TryGetValue("room.creation.name.too_short"));
                return null;
            }

            int roomId = 0;

            using (IQueryAdapter dbClient = PlusEnvironment.GetDatabaseManager().GetQueryReactor())
            {
                dbClient.SetQuery("INSERT INTO `rooms` (`roomtype`,`caption`,`description`,`owner`,`model_name`,`category`,`users_max`,`trade_settings`) VALUES ('private',@caption,@description,@UserId,@model,@category,@usersmax,@tradesettings)");
                dbClient.AddParameter("caption", name);
                dbClient.AddParameter("description", description);
                dbClient.AddParameter("UserId", session.GetHabbo().Id);
                dbClient.AddParameter("model", model.Id);
                dbClient.AddParameter("category", category);
                dbClient.AddParameter("usersmax", maxVisitors);
                dbClient.AddParameter("tradesettings", tradeSettings);

                roomId = Convert.ToInt32(dbClient.InsertQuery());
            }


            RoomData data = new RoomData(roomId, name, model.Id, session.GetHabbo().Username, session.GetHabbo().Id, "", 0, "public", "open", 0, maxVisitors, category, description, string.Empty,
             floor, landscape, 1, 1, 0, 0, wallthick, floorthick, wallpaper, 1, 1, 1, 1, 1, 1, 1, 8, tradeSettings, true, true, true, true, true, true, true, 0, 0, true, model);

            return data;
        }

        public ICollection<Room> GetRooms()
        {
            return this._rooms.Values;
        }

        public void Dispose()
        {
            int length = _rooms.Count;
            int i = 0;
            foreach (Room Room in this._rooms.Values.ToList())
            {
                if (Room == null)
                    continue;

                PlusEnvironment.GetGame().GetRoomManager().UnloadRoom(Room.Id);
                Console.Clear();
                log.Info("<<- SERVER SHUTDOWN ->> ROOM ITEM SAVE: " + String.Format("{0:0.##}", ((double)i / length) * 100) + "%");
                i++;
            }
            log.Info("Done disposing rooms!");
        }
    }
}