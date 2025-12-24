using System;
using System.Collections.Generic;
using System.Linq;
using BattleLog.Game;
using BattleLog.Game.PacketHeaders;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;

namespace BattleLog.Tracker;

public class EventTracker
{
    public class StatusEvent()
    {
        public DateTime Timestamp { get; set; }
        public uint EntityId { get; set; }
        public ushort StatusId { get; set; }
        public float Duration { get; set; }
        public ushort Stacks { get; set; }
    }

    public Dictionary<string, (uint NameId, ushort CastId)> actorCastsDict;
    public List<StatusEvent> statusEffects;
    private readonly IPluginLog pluginLog;
    private readonly ExcelNameFinder _excelNameFinder;
    private readonly IObjectTable objectTable;

    public EventTracker(
        IPluginLog pluginLog,
        Configuration configuration,
        ExcelNameFinder excelNameFinder,
        IObjectTable objectTable
    )
    {
        this.pluginLog = pluginLog;
        this._excelNameFinder = excelNameFinder;
        this.objectTable = objectTable;
        this.actorCastsDict = new();
        this.statusEffects = new();
    }

    public void AddActorCast(ActorCast actorCast, uint sourceId)
    {
        var gameObject = objectTable.FirstOrDefault(x => x.EntityId == sourceId);
        if (gameObject is IBattleNpc battleNpc)
        {
            this.actorCastsDict[$"{actorCast.actionId}-{battleNpc.NameId}"] = (
                battleNpc.NameId,
                actorCast.actionId
            );
        }
    }

    public void AddStatusEffect(EffectResultEntry statusEffect, uint sourceId)
    {
        //var gameObject = objectTable.FirstOrDefault(x => x.EntityId == sourceId);
        var localPlayer = objectTable.LocalPlayer;
        //pluginLog.Debug($"ASE1 {statusEffect.duration} {statusEffect.stacks} {statusEffect.srcActorId} {statusEffect.statusId} {sourceId}");
        if (
            localPlayer is not null
            && (localPlayer.EntityId == sourceId || localPlayer.CastTargetObjectId == sourceId)
        )
        {
            var status = _excelNameFinder.GetStatusById(statusEffect.statusId);
            if (status is null || status.Value.StatusCategory == 1)
            {
                return;
            }

            pluginLog.Debug(
                $"{DateTime.Now.ToShortTimeString()} {GetStatusNameById(statusEffect.statusId)} on {sourceId} for {statusEffect.duration}"
            );
            statusEffects.Add(
                new StatusEvent
                {
                    EntityId = sourceId,
                    Duration = statusEffect.duration,
                    Stacks = statusEffect.stacks,
                    StatusId = statusEffect.statusId,
                    Timestamp = DateTime.Now,
                }
            );
        }
    }

    public string GetActionNameById(ushort castId)
    {
        return _excelNameFinder.GetActionById(castId)?.Name.ExtractText() ?? "N/A";
    }

    public string GetBNpcNameById(uint nameId)
    {
        return _excelNameFinder.GetBNpcNameById(nameId)?.Singular.ExtractText() ?? "N/A";
    }

    public string GetStatusNameById(uint statusId)
    {
        return _excelNameFinder.GetStatusById(statusId)?.Name.ExtractText() ?? "N/A";
    }
}
