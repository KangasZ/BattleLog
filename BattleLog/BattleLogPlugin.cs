using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using BattleLog.Game;
using BattleLog.Tracker;
using BattleLog.UI;
using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Utility.Timing;

namespace BattleLog;

public class BattleLogPlugin : IDalamudPlugin
{
    public string Name => "BattleLog";
    private readonly Configuration PluginConfiguration;
    private readonly IPluginLog pluginLog;
    private readonly NetworkDecoder networkDecoder;
    private readonly GameNetwork gameNetwork;
    private readonly ExcelNameFinder _excelNameFinder;
    private readonly EventTracker eventTracker;
    private readonly EventTrackerWindow eventTrackerWindow;
    private readonly ICommandManager commandManager;
    public static string GameVersion { get; private set; } = "";

    public BattleLogPlugin(
        IDalamudPluginInterface pluginInterface,
        IPluginLog pluginLog,
        IGameInteropProvider gameInteropProvider,
        IDataManager dataManager,
        IObjectTable objectTable,
        ICommandManager commandManager
    )
    {
        this.pluginLog = pluginLog;
        this.commandManager = commandManager;
        this._excelNameFinder = new ExcelNameFinder(dataManager, pluginLog);
        PluginConfiguration =
            pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        PluginConfiguration.Initialize(pluginInterface);
        GameVersion = GetGameVersion();
        gameNetwork = new GameNetwork(gameInteropProvider, pluginLog);
        this.eventTracker = new EventTracker(
            pluginLog,
            PluginConfiguration,
            _excelNameFinder,
            objectTable
        );

        this.networkDecoder = new NetworkDecoder(
            pluginLog,
            PluginConfiguration,
            gameNetwork,
            this.eventTracker,
            _excelNameFinder
        );

        this.eventTrackerWindow = new EventTrackerWindow(pluginInterface, eventTracker);

        this.commandManager.AddHandler(
            "/battlelog",
            new CommandInfo((ev, ev2) => eventTrackerWindow.OpenUi())
            {
                HelpMessage = "Opens main window",
                ShowInHelp = true,
            }
        );
    }

    private string GetGameVersion()
    {
        var gameVersion = "";
        FileInfo fi = new FileInfo(Process.GetCurrentProcess().MainModule.FileName);
        DirectoryInfo di = fi.Directory;
        string fullproc = Path.Combine(di.FullName, "ffxivgame.ver");
        if (File.Exists(fullproc))
        {
            gameVersion = File.ReadAllText(fullproc).Trim();
            pluginLog.Debug($"Game version is {0}", gameVersion);
        }
        else
        {
            pluginLog.Debug("file {0} doesn't exist", fullproc);
        }

        return gameVersion;
    }

    public void Dispose()
    {
        PluginConfiguration.Dispose();
        networkDecoder.Dispose();
        gameNetwork.Dispose();
        commandManager.RemoveHandler("/battlelog");
    }
}
