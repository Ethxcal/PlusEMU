﻿using System.Data;
using Microsoft.Extensions.Logging;
using Plus.Utilities;

namespace Plus.HabboHotel.Items.Televisions;

public class TelevisionManager : ITelevisionManager
{
    private readonly ILogger<TelevisionManager> _logger;

    public TelevisionManager(ILogger<TelevisionManager> logger)
    {
        _logger = logger;
    }

    public Dictionary<int, TelevisionItem> Televisions { get; } = new();


    public ICollection<TelevisionItem> TelevisionList => Televisions.Values;

    public void Init()
    {
        if (Televisions.Count > 0)
            Televisions.Clear();
        DataTable getData = null;
        using (var dbClient = PlusEnvironment.GetDatabaseManager().GetQueryReactor())
        {
            dbClient.SetQuery("SELECT * FROM `items_youtube` ORDER BY `id` DESC");
            getData = dbClient.GetTable();
            if (getData != null)
            {
                foreach (DataRow row in getData.Rows)
                {
                    Televisions.Add(Convert.ToInt32(row["id"]),
                        new TelevisionItem(Convert.ToInt32(row["id"]), row["youtube_id"].ToString(), row["title"].ToString(), row["description"].ToString(),
                            ConvertExtensions.EnumToBool(row["enabled"].ToString())));
                }
            }
        }
        _logger.LogInformation("Television Items -> LOADED");
    }

    public bool TryGet(int itemId, out TelevisionItem televisionItem)
    {
        if (Televisions.TryGetValue(itemId, out televisionItem))
            return true;
        return false;
    }
}