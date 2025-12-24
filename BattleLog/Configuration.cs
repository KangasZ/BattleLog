using System;
using Dalamud.Configuration;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace BattleLog;

[Serializable]
public class Configuration : IPluginConfiguration
{
    [NonSerialized]
    private IDalamudPluginInterface? pluginInterface;
    public int Version { get; set; } = 0;
    public bool Enabled = true;

    public string OpcodeUrl =
        @"https://raw.githubusercontent.com/paissaheavyindustries/Resources/refs/heads/main/Blueprint/blueprint.xml";
    public string OpcodeRegion = "EN/DE/FR/JP";

    public void Initialize(IDalamudPluginInterface pluginInterface)
    {
        this.pluginInterface = pluginInterface;
    }

    public void Save()
    {
        if (this.pluginInterface == null)
            return;
        pluginInterface.SavePluginConfig(this);
    }

    public void Dispose() => Save();
}
