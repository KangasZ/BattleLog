using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using BattleLog.Game.PacketHeaders;
using BattleLog.Tracker;
using Dalamud.Game.Network;
using Dalamud.Plugin.Services;

namespace BattleLog.Game;

internal class NetworkDecoder : IDisposable
{
    internal class StatusTracker
    {
        public delegate void StatusGainDelegate(
            uint srcActorId,
            uint actorId,
            uint statusId,
            float duration,
            int stacks
        );

        public delegate void StatusLoseDelegate(uint actorId, uint statusId);

        public event StatusGainDelegate OnStatusGain;
        public event StatusLoseDelegate OnStatusLose;

        internal struct Entry
        {
            public uint srcActorId;
            public uint actorId;
            public uint statusId;
            public float duration;
            public int stacks;
            public ulong runNumber;
        }

        public Dictionary<uint, Dictionary<uint, Entry>> entries =
            new Dictionary<uint, Dictionary<uint, Entry>>();
        public ulong runNumber = 1;

        private void ApplyStatus(Entry e)
        {
            lock (this)
            {
                Dictionary<uint, Entry> actor;
                Entry exentry;
                e.runNumber = runNumber;
                if (entries.TryGetValue(e.actorId, out actor) == false)
                {
                    actor = new Dictionary<uint, Entry>();
                    entries[e.actorId] = actor;
                }

                if (actor.TryGetValue(e.statusId, out exentry) == false)
                {
                    actor[e.statusId] = e;
                    OnStatusGain?.Invoke(e.srcActorId, e.actorId, e.statusId, e.duration, e.stacks);
                }
                else
                {
                    if (e.stacks != exentry.stacks || e.duration > exentry.duration)
                    {
                        actor[e.statusId] = e;
                        OnStatusGain?.Invoke(
                            e.srcActorId,
                            e.actorId,
                            e.statusId,
                            e.duration,
                            e.stacks
                        );
                    }
                    else
                    {
                        actor[e.statusId] = e;
                    }
                }
            }
        }

        public void ApplyStatus(IEnumerable<Entry> newEntries)
        {
            lock (this)
            {
                foreach (Entry e in newEntries)
                {
                    ApplyStatus(e);
                }
            }
        }

        public void ReplaceStatusForActor(uint actorId, IEnumerable<Entry> newEntries)
        {
            lock (this)
            {
                Dictionary<uint, Entry> actor;
                if (newEntries == null)
                {
                    if (entries.TryGetValue(actorId, out actor) == true)
                    {
                        foreach (KeyValuePair<uint, Entry> kp in actor)
                        {
                            OnStatusLose?.Invoke(actorId, kp.Value.statusId);
                        }

                        entries.Remove(actorId);
                    }
                }
                else
                {
                    runNumber++;
                    ApplyStatus(newEntries);
                    if (entries.TryGetValue(actorId, out actor) == true)
                    {
                        List<Entry> toRem = (
                            from ix in actor.Values
                            where ix.runNumber != runNumber
                            select ix
                        ).ToList();
                        foreach (Entry e in toRem)
                        {
                            actor.Remove(e.statusId);
                            OnStatusLose?.Invoke(actorId, e.statusId);
                        }
                    }
                }
            }
        }
    }

    internal class OpcodeList
    {
        internal ushort StatusEffectList = 0;
        internal ushort StatusEffectList2 = 0;
        internal ushort StatusEffectList3 = 0;
        internal ushort BossStatusEffectList = 0;
        internal ushort Ability1 = 0;
        internal ushort Ability8 = 0;
        internal ushort Ability16 = 0;
        internal ushort Ability24 = 0;
        internal ushort Ability32 = 0;
        internal ushort ActorCast = 0;
        internal ushort EffectResult = 0;
        internal ushort ActorControl = 0;
        internal ushort ActorControlSelf = 0;
        internal ushort ActorControlTarget = 0;
        internal ushort MapEffect = 0;
        internal ushort EventPlay = 0;
        internal ushort EventPlay64 = 0;
    }

    internal enum DirectorTypeEnum : uint
    {
        Init = 0x40000001,
        FadeOut = 0x40000005,
        BarrierDown = 0x40000006,
    }

    internal OpcodeList Opcodes;
    internal StatusTracker _tracker;

    private List<string> _opcodeRegions = null;
    private Blueprint.Region _nextOpcodeRegion = null;
    private Blueprint.Region _currentOpcodeRegion = null;
    private Blueprint _blueprint = null;
    private readonly IPluginLog pluginLog;
    private readonly Configuration configuration;
    private readonly GameNetwork gameNetwork;
    public bool StatusGotOpcodes = false;
    private readonly EventTracker eventTracker;
    private readonly ExcelNameFinder excelNameFinder;

    public NetworkDecoder(
        IPluginLog pluginLog,
        Configuration configuration,
        GameNetwork gameNetwork,
        EventTracker eventTracker,
        ExcelNameFinder excelNameFinder
    )
    {
        this.pluginLog = pluginLog;
        this.configuration = configuration;
        this.gameNetwork = gameNetwork;
        this.eventTracker = eventTracker;
        this.excelNameFinder = excelNameFinder;
        // _tracker = new StatusTracker();
        // _tracker.OnStatusGain += _tracker_OnStatusGain;
        // _tracker.OnStatusLose += _tracker_OnStatusLose;
        StatusGotOpcodes = GetOpcodes(true); // TODO: Fallback
        SetOpcodeRegion(this.configuration.OpcodeRegion);
        gameNetwork.NetworkMessage += NetworkMessageReceived;
    }

    public void Dispose()
    {
        gameNetwork.NetworkMessage -= NetworkMessageReceived;
        // _tracker.OnStatusGain -= _tracker_OnStatusGain;
        // _tracker.OnStatusLose -= _tracker_OnStatusLose;
        // _tracker = null;
    }

    internal string GetOpcodeVersion()
    {
        if (_currentOpcodeRegion == null)
        {
            return null;
        }

        return _currentOpcodeRegion.Version;
    }

    internal List<Blueprint.Region.Warning> GetOpcodeWarnings()
    {
        if (_currentOpcodeRegion == null)
        {
            return null;
        }

        return _currentOpcodeRegion.Warnings;
    }

    internal ushort GetOpcodeForRegion(Blueprint.Region region, string id)
    {
        if (region.OpcodeLookup.TryGetValue(id, out Blueprint.Region.Opcode val) == true)
        {
            return val.Id;
        }

        return 0;
    }

    internal void SetOpcodes(Blueprint.Region region)
    {
        Opcodes = new OpcodeList();
        Opcodes.StatusEffectList = GetOpcodeForRegion(region, "StatusEffectList");
        Opcodes.StatusEffectList2 = GetOpcodeForRegion(region, "StatusEffectList2");
        Opcodes.StatusEffectList3 = GetOpcodeForRegion(region, "StatusEffectList3");
        Opcodes.BossStatusEffectList = GetOpcodeForRegion(region, "BossStatusEffectList");
        Opcodes.Ability1 = GetOpcodeForRegion(region, "Ability1");
        Opcodes.Ability8 = GetOpcodeForRegion(region, "Ability8");
        Opcodes.Ability16 = GetOpcodeForRegion(region, "Ability16");
        Opcodes.Ability24 = GetOpcodeForRegion(region, "Ability24");
        Opcodes.Ability32 = GetOpcodeForRegion(region, "Ability32");
        Opcodes.ActorCast = GetOpcodeForRegion(region, "ActorCast");
        Opcodes.EffectResult = GetOpcodeForRegion(region, "EffectResult");
        Opcodes.MapEffect = GetOpcodeForRegion(region, "MapEffect");
        Opcodes.EventPlay = GetOpcodeForRegion(region, "EventPlay");
        Opcodes.EventPlay64 = GetOpcodeForRegion(region, "EventPlay64");
        Opcodes.ActorControl = GetOpcodeForRegion(region, "ActorControl");
        Opcodes.ActorControlSelf = GetOpcodeForRegion(region, "ActorControlSelf");
        Opcodes.ActorControlTarget = GetOpcodeForRegion(region, "ActorControlTarget");
        pluginLog.Debug(
            "Opcodes set to: {0} {1} {2} {3} {4} {5} {6} {7} {8} {9} {10} {11} {12} {13} {14} {15} {16}",
            Opcodes.StatusEffectList,
            Opcodes.StatusEffectList2,
            Opcodes.StatusEffectList3,
            Opcodes.BossStatusEffectList,
            Opcodes.Ability1,
            Opcodes.Ability8,
            Opcodes.Ability16,
            Opcodes.Ability24,
            Opcodes.Ability32,
            Opcodes.ActorCast,
            Opcodes.EffectResult,
            Opcodes.MapEffect,
            Opcodes.EventPlay,
            Opcodes.EventPlay64,
            Opcodes.ActorControl,
            Opcodes.ActorControlSelf,
            Opcodes.ActorControlTarget
        );
    }

    internal static void SetOpcode(IDictionary<string, ushort> src, string name, out ushort opcode)
    {
        ushort temp;
        if (src.TryGetValue(name, out temp) == true)
        {
            opcode = temp;
        }
        else
        {
            opcode = 0;
        }
    }

    internal void DecodeActorControl(
        ActorControlCategory category,
        uint sourceActorId,
        uint targetActorId,
        uint param1,
        uint param2,
        uint param3,
        uint param4
    )
    {
        switch (category)
        {
            case ActorControlCategory.Headmarker:
                // param1 = headmarker id
                //_st.InvokeHeadmarker(targetActorId, param1);
                break;
            case ActorControlCategory.Tether:
                // param2 = tether type
                // param3 = tether buddy id
                //_st.InvokeTether(targetActorId, param3, param2);
                break;
            case ActorControlCategory.Director:
                if (param2 == (uint)DirectorTypeEnum.FadeOut)
                {
                    // _st._runInstance++;
                    // if (_st.cfg.RemoveMarkersAfterWipe == true)
                    // {
                    //     _st._suppressCombatEndMarkRemoval = true;
                    // }
                }

                if (param2 == (uint)DirectorTypeEnum.BarrierDown)
                {
                    // if (_st.cfg.RemoveMarkersAfterWipe == true)
                    // {
                    //     _st.Log(State.LogLevelEnum.Debug, null, "Wiped, removing markers");
                    //     _st.ClearAutoMarkers();
                    // }
                    // _st.AutoselectTimeline(_st.cs.TerritoryType);
                }

                if (param2 == (uint)DirectorTypeEnum.Init)
                {
                    // _st.AutoselectTimeline(_st.cs.TerritoryType);
                }

                // _st.InvokeDirectorUpdate(param1, param2, param3, param4);
                break;
            case ActorControlCategory.Sign:
                break;
            default:
                // _st.InvokeActorControl((ushort)category, sourceActorId, targetActorId, param1, param2, param3, param4);
                break;
        }
    }

    internal unsafe void Decode(nint dataPtr, ushort opCode, uint sourceActorId, uint targetActorId)
    {
        //pluginLog.Verbose("Opcode: {0} (source: {1:X8}, target: {2:X8})", opCode, sourceActorId, targetActorId);
        if (opCode == Opcodes.ActorCast)
        {
            ActorCast ac = Marshal.PtrToStructure<ActorCast>(dataPtr);
            // _st.InvokeCastBegin(targetActorId, ac.targetId, ac.actionId, ac.castTime, ac.rotation);
            eventTracker.AddActorCast(ac, targetActorId);
        }
        else if (opCode == Opcodes.ActorControl)
        {
            // ActorControl ac = Marshal.PtrToStructure<ActorControl>(dataPtr);
            // DecodeActorControl(ac.category, sourceActorId, targetActorId, ac.param1, ac.param2, ac.param3, ac.param4);
        }
        else if (opCode == Opcodes.ActorControlSelf)
        {
            // ActorControlSelf ac = Marshal.PtrToStructure<ActorControlSelf>(dataPtr);
            // DecodeActorControl(ac.category, sourceActorId, targetActorId, ac.param1, ac.param2, ac.param3, ac.param4);
        }
        else if (opCode == Opcodes.ActorControlTarget)
        {
            // ActorControlTarget ac = Marshal.PtrToStructure<ActorControlTarget>(dataPtr);
            // DecodeActorControl(ac.category, sourceActorId, ac.targetId, ac.param1, ac.param2, ac.param3, ac.param4);
        }
        else if (opCode == Opcodes.Ability1)
        {
            // Ability1 ac = Marshal.PtrToStructure<Ability1>(dataPtr);
            // _st.InvokeAction(targetActorId, (uint)ac.targetId[0], ac.actionId);
        }
        else if (opCode == Opcodes.Ability8)
        {
            // Ability8 ac = Marshal.PtrToStructure<Ability8>(dataPtr);
            // for (int i = 0; i < 8; i++)
            // {
            //     // _st.InvokeAction(targetActorId, (uint)ac.targetId[i], ac.actionId);
            // }
        }
        else if (opCode == Opcodes.Ability16)
        {
            // Ability16 ac = Marshal.PtrToStructure<Ability16>(dataPtr);
            // for (int i = 0; i < 16; i++)
            // {
            //     // _st.InvokeAction(targetActorId, (uint)ac.targetId[i], ac.actionId);
            // }
        }
        else if (opCode == Opcodes.Ability24)
        {
            // Ability24 ac = Marshal.PtrToStructure<Ability24>(dataPtr);
            // for (int i = 0; i < 24; i++)
            // {
            //     // _st.InvokeAction(targetActorId, (uint)ac.targetId[i], ac.actionId);
            // }
        }
        else if (opCode == Opcodes.Ability32)
        {
            //Ability32 ac = Marshal.PtrToStructure<Ability32>(dataPtr);
            //for (int i = 0; i < 32; i++)
            // {
            // _st.InvokeAction(targetActorId, (uint)ac.targetId[i], ac.actionId);
            // }
        }
        else if (opCode == Opcodes.EffectResult)
        {
            EffectResult ac = Marshal.PtrToStructure<EffectResult>(dataPtr);
            EffectResultEntry* ae = (EffectResultEntry*)(dataPtr + 28);
            for (int i = 0; i < ac.entryCount; i++)
            {
                if (ae[i].statusId > 0)
                {
                    var curr = ae[i];
                    //pluginLog.Debug($"Result {curr.duration} {curr.stacks} {curr.srcActorId} {curr.statusId}");
                    eventTracker.AddStatusEffect(curr, targetActorId);
                }
            }
        }
        else if (opCode == Opcodes.StatusEffectList)
        {
            StatusEffectList ac = Marshal.PtrToStructure<StatusEffectList>(dataPtr);
            HandleStatusEffectList((StatusEffectListEntry*)(dataPtr + 20), null, targetActorId);
        }
        else if (opCode == Opcodes.StatusEffectList2)
        {
            StatusEffectList2 ac = Marshal.PtrToStructure<StatusEffectList2>(dataPtr);
            HandleStatusEffectList((StatusEffectListEntry*)(dataPtr + 24), null, targetActorId);
        }
        else if (opCode == Opcodes.StatusEffectList3)
        {
            StatusEffectList3 ac = Marshal.PtrToStructure<StatusEffectList3>(dataPtr);
            HandleStatusEffectList((StatusEffectListEntry*)(dataPtr + 0), null, targetActorId);
        }
        else if (opCode == Opcodes.BossStatusEffectList)
        {
            StatusEffectList ac = Marshal.PtrToStructure<StatusEffectList>(dataPtr);
            HandleStatusEffectList(
                (StatusEffectListEntry*)(dataPtr + 0),
                (StatusEffectListEntry*)(dataPtr + 380),
                targetActorId
            );
        }
        else if (opCode == Opcodes.MapEffect)
        {
            byte[] bytes = new byte[11];
            //Marshal.Copy(dataPtr, bytes, 0, bytes.Length);
            //_st.InvokeMapEffect(bytes);
        }
        else if (opCode == Opcodes.EventPlay)
        {
            //EventPlay ac = Marshal.PtrToStructure<EventPlay>(dataPtr);
            //_st.InvokeEventPlay((uint)ac.actorId, ac.eventId, ac.scene, ac.flags, ac.param1, ac.param2, ac.param3, ac.param4);
        }
        else if (opCode == Opcodes.EventPlay64)
        {
            //_st.InvokeEventPlay64();
        }
        // else if (_st.cfg.LogUnhandledOpcodes == true)
        // {
        //     _st.Log(State.LogLevelEnum.Debug, null, "Unhandled opcode: {0} (source: {1:X8}, target: {2:X8})", opCode, sourceActorId, targetActorId);
        // }
    }

    internal unsafe void HandleStatusEffectList(
        StatusEffectListEntry* ae1,
        StatusEffectListEntry* ae2,
        uint targetActorId
    )
    {
        /*
        pluginLog.Debug($"Result {ae1->duration} {ae1->stacks} {ae1->srcActorId} {ae1->statusId}");
        List<StatusTracker.Entry> entries = new List<StatusTracker.Entry>();
        List<string> temp = new List<string>();
        ushort seed = ae1[29].statusId;
        temp.Add(string.Format("[{0}/{1:X}]", seed, targetActorId));
        for (int i = 0; i < 30; i++)
        {
            temp.Add(string.Format("{0}={1}", i, ae1[i].statusId - seed));
            if (ae1[i].statusId - seed > 0)
            {
                var srcActorId = ae1[i].srcActorId;
                var statusId = (uint)ae1[i].statusId - seed;
                var duration = Math.Abs(ae1[i].duration);
                var stacks = ae1[i].stacks;
                pluginLog.Debug($"Result {duration} {stacks} {srcActorId} {targetActorId} {statusId} {excelNameFinder.GetStatusById(statusId)?.Name.ExtractText() ?? "N/A"}");
                
            }
        }
        */
        //
        // if (ae2 != null)
        // {
        //     for (int i = 0; i < 30; i++)
        //     {
        //         temp.Add(string.Format("{0}={1}", i, ae2[i].statusId - seed));
        //         if (ae2[i].statusId - seed > 0)
        //         {
        //             entries.Add(
        //                 new StatusTracker.Entry()
        //                 {
        //                     srcActorId = ae2[i].srcActorId, actorId = targetActorId,
        //                     statusId = (uint)ae2[i].statusId - seed, duration = Math.Abs(ae2[i].duration),
        //                     stacks = ae2[i].stacks
        //                 }
        //             );
        //         }
        //     }
        // }
        //
        // _tracker.ReplaceStatusForActor(targetActorId, entries.Count > 0 ? entries : null);
    }

    internal IEnumerable<string> GetOpcodeRegions()
    {
        return _opcodeRegions;
    }

    internal Blueprint GetBlueprintFromURI(string uri)
    {
        try
        {
            XmlDocument doc;
            Uri u = new Uri(uri);
            if (u.IsFile == true)
            {
                doc = new XmlDocument();
                doc.Load(uri);
            }
            else
            {
                using HttpClient http = new HttpClient();
                using HttpRequestMessage req = new HttpRequestMessage()
                {
                    Method = HttpMethod.Get,
                    RequestUri = u,
                };
                using HttpResponseMessage resp = http.Send(req);
                if (resp.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    pluginLog.Error(
                        "Couldn't load blueprint from {0}, response code was: {1}",
                        uri,
                        resp.StatusCode
                    );
                    return null;
                }

                using StreamReader sr = new StreamReader(resp.Content.ReadAsStream());
                string data = sr.ReadToEnd();
                doc = new XmlDocument();
                doc.LoadXml(data);
            }

            return (Blueprint)DeserializeXml<Blueprint>(doc);
        }
        catch (Exception ex)
        {
            pluginLog.Error("Couldn't load retrieve blueprint from {0} {1}", uri, ex);
        }

        return null;
    }

    internal bool GetOpcodes(bool fallback)
    {
        try
        {
            bool fromBackup = false;
            pluginLog.Debug("Loading blueprint from {0}", configuration.OpcodeUrl);
            Blueprint bp = GetBlueprintFromURI(configuration.OpcodeUrl);
            if (bp == null && fallback == true)
            {
                try
                {
                    string temp = Path.GetTempPath();
                    string tempfile = Path.Combine(temp, "lemegeton_blueprint.xml");
                    pluginLog.Debug("Loading blueprint backup from {0}", tempfile);
                    XmlDocument tempdoc = new XmlDocument();
                    tempdoc.Load(tempfile);
                    bp = (Blueprint)DeserializeXml<Blueprint>(tempdoc);
                    pluginLog.Debug("Blueprint backup loaded");
                    fromBackup = true;
                }
                catch (Exception ex)
                {
                    pluginLog.Error("Couldn't load blueprint backup");
                }
            }

            if (bp != null)
            {
                bp.BuildLookups();
                _blueprint = bp;
                if (fromBackup == false)
                {
                    try
                    {
                        string temp = Path.GetTempPath();
                        string tempfile = Path.Combine(temp, "lemegeton_blueprint.xml");
                        XmlDocument tempdoc = SerializeXml<Blueprint>(bp);
                        File.WriteAllText(tempfile, tempdoc.OuterXml);
                        pluginLog.Debug("Blueprint backup saved to {0}", tempfile);
                    }
                    catch (Exception ex)
                    {
                        pluginLog.Error("Couldn't save blueprint backup");
                    }
                }

                _opcodeRegions = new List<string>(from ix in bp.RegionLookup select ix.Key);
                return true;
            }
        }
        catch (Exception ex)
        {
            pluginLog.Error("Couldn't retrieve blueprint");
        }

        return false;
    }

    internal void SetOpcodeRegion(string name)
    {
        if (_blueprint.RegionLookup.ContainsKey(name) == true)
        {
            Blueprint.Region r = _blueprint.RegionLookup[name];
            pluginLog.Info("Setting opcode region to {0} ({1})", r.Name, r.Version);
            if (
                !string.Equals(
                    r.Version,
                    BattleLogPlugin.GameVersion,
                    StringComparison.InvariantCultureIgnoreCase
                )
            )
            {
                pluginLog.Warning(
                    "Opcode version {0} and game version {1} differ, things may be broken",
                    r.Version,
                    BattleLogPlugin.GameVersion
                );
            }

            _nextOpcodeRegion = r;
            return;
        }

        string defName = "EN/DE/FR/JP";
        if (_blueprint.RegionLookup.ContainsKey(defName) == true)
        {
            Blueprint.Region r = _blueprint.RegionLookup[defName];
            pluginLog.Warning(
                "Couldn't set opcode region to {0}, defaulting to {1} ({2})",
                name,
                r.Name,
                r.Version
            );
            _nextOpcodeRegion = r;
            return;
        }

        Blueprint.Region reg = _blueprint.Regions.First();
        pluginLog.Warning(
            "Couldn't set opcode region to {0}, defaulting to first found {1} ({2})",
            name,
            reg.Name,
            reg.Version
        );
        _nextOpcodeRegion = reg;
    }

    internal void NetworkMessageReceived(
        nint dataPtr,
        ushort opCode,
        uint sourceActorId,
        uint targetActorId,
        NetworkMessageDirection direction
    )
    {
        if (direction != NetworkMessageDirection.ZoneDown)
        {
            return;
        }

        if (_nextOpcodeRegion != null)
        {
            SetOpcodes(_nextOpcodeRegion);
            _currentOpcodeRegion = _nextOpcodeRegion;
            _nextOpcodeRegion = null;
        }

        Decode(dataPtr, opCode, sourceActorId, targetActorId);
    }

    private void _tracker_OnStatusGain(
        uint srcActorId,
        uint actorId,
        uint statusId,
        float duration,
        int stacks
    )
    {
        //_st.InvokeStatusChange(srcActorId, actorId, statusId, true, duration, stacks);
    }

    private void _tracker_OnStatusLose(uint actorId, uint statusId)
    {
        //_st.InvokeStatusChange(0, actorId, statusId, false, 0.0f, 0);
    }

    internal object DeserializeXml<T>(XmlDocument doc)
    {
        try
        {
            XmlSerializer xs = new XmlSerializer(typeof(T));
            byte[] buf = UTF8Encoding.UTF8.GetBytes(doc.OuterXml);
            using (MemoryStream ms = new MemoryStream(buf))
            {
                T o = (T)xs.Deserialize(ms);
                return o;
            }
        }
        catch (Exception ex)
        {
            pluginLog.Error("Error deserializing XML: {0}", ex.Message);
        }

        return null;
    }

    internal XmlDocument SerializeXml<T>(object o)
    {
        XmlSerializerNamespaces ns = new XmlSerializerNamespaces();
        ns.Add("", "");
        XmlSerializer xs = new XmlSerializer(typeof(T));
        string temp = "";
        using (MemoryStream ms = new MemoryStream())
        {
            xs.Serialize(ms, o, ns);
            ms.Position = 0;
            using (StreamReader sr = new StreamReader(ms))
            {
                temp = sr.ReadToEnd();
            }
        }

        XmlDocument doc = new XmlDocument();
        doc.LoadXml(temp);
        return doc;
    }
}
