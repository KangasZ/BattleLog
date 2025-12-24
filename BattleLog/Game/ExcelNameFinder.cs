using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace BattleLog.Game;

public class ExcelNameFinder
{
    private readonly IPluginLog pluginLog;
    private readonly Dictionary<uint, Action> actions;
    private readonly Dictionary<uint, BNpcName> bNpcNames;
    private readonly Dictionary<uint, Status> status;

    public ExcelNameFinder(IDataManager dataManager, IPluginLog pluginLog)
    {
        this.pluginLog = pluginLog;
        this.actions = dataManager
            .Excel.GetSheet<Lumina.Excel.Sheets.Action>()
            .ToDictionary(x => x.RowId, x => x);
        this.bNpcNames = dataManager
            .Excel.GetSheet<Lumina.Excel.Sheets.BNpcName>()
            .ToDictionary(x => x.RowId, x => x);
        this.status = dataManager
            .Excel.GetSheet<Lumina.Excel.Sheets.Status>()
            .ToDictionary(x => x.RowId, x => x);
    }

    public Action? GetActionById(uint actionId)
    {
        if (actions.TryGetValue(actionId, out var action))
        {
            return action;
        }
        return null;
    }

    public BNpcName? GetBNpcNameById(uint bNpcId)
    {
        if (bNpcNames.TryGetValue(bNpcId, out var bNpcName))
        {
            return bNpcName;
        }
        return null;
    }

    public Status? GetStatusById(uint statusId)
    {
        if (status.TryGetValue(statusId, out var statusObject))
        {
            return statusObject;
        }

        return null;
    }
}
