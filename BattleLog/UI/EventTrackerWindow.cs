using System;
using System.Numerics;
using BattleLog.Tracker;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin;

namespace BattleLog.UI;

public class EventTrackerWindow : IDisposable
{
    /*
     * ^20\|(?:[^|]*\|){3}<castidhere>\|
     * ^20\|[^|]*\|[^|]*\|[^|]*\|CASTIDHERE\|
     *
     *
     *
     *
     *
    <?xml version="1.0"?>
    <TriggernometryExport PluginVersion="1.2.0.7">
      <ExportedTrigger Enabled="true" Source="FFXIVNetwork" Sequential="True" Name="Blizzard Debuff" Id="bcbbc6c7-9d58-4142-a9d7-29b508665c82" RegularExpression="^26\|[^|]*\|99E\|[^|]*\|(?&lt;debufftimer&gt;[^|]*)\|[^|]*\|[^|]*\|[^|]*\|(?&lt;name&gt;[^|]*)\|">
        <Condition Enabled="true" Grouping="And">
          <ConditionSingle Enabled="true" ExpressionL="${name}" ExpressionTypeL="String" ExpressionR="${_ffxivplayer}" ExpressionTypeR="String" ConditionType="StringEqualNocase" />
          <ConditionSingle Enabled="true" ExpressionL="${debufftimer}" ExpressionTypeL="String" ExpressionR="21.00" ExpressionTypeR="String" ConditionType="StringEqualNocase" />
        </Condition>
      </ExportedTrigger>
    </TriggernometryExport>
     */
    private bool mainWindowVisible = false;
    private readonly EventTracker eventTracker;
    private readonly IDalamudPluginInterface pluginInterface;

    public EventTrackerWindow(IDalamudPluginInterface pluginInterface, EventTracker eventTracker)
    {
        this.eventTracker = eventTracker;
        this.pluginInterface = pluginInterface;
        this.pluginInterface.UiBuilder.Draw += Draw;
        this.pluginInterface.UiBuilder.OpenConfigUi += OpenUi;
    }

    public void Dispose()
    {
        this.pluginInterface.UiBuilder.Draw -= Draw;
        this.pluginInterface.UiBuilder.OpenConfigUi -= OpenUi;
    }

    public void OpenUi()
    {
        mainWindowVisible = true;
    }

    private void Draw()
    {
        if (!mainWindowVisible)
        {
            return;
        }

        var size = new Vector2(600, 600);
        ImGui.SetNextWindowSize(size, ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(size, new Vector2(float.MaxValue, float.MaxValue));

        if (ImGui.Begin("Event Tracker##event-tracker-main", ref mainWindowVisible))
        {
            DrawTabs(
                "event-tracker-tabs",
                ("Actor Casts", 0xFFFFFFFF, DrawActorCasts),
                ("Self Status Effects", 0xFFFFFFFF, DrawStatusEffects)
            );
        }

        ImGui.End();
    }

    public void DrawStatusEffects()
    {
        ImGui.Text("Tracked Status Effects");
        ImGui.SameLine();
        if (ImGui.Button("Clear Tracked"))
        {
            eventTracker.statusEffects.Clear();
        }
        ImGui.BeginTable(
            $"statuseffects",
            7,
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Sortable
        );
        /*
         *                 EntityId = sourceId,
                Duration = statusEffect.duration,
                Stacks = statusEffect.stacks,
                StatusId = statusEffect.statusId,
                Timestamp = DateTime.Now
         */
        ImGui.TableSetupColumn("Timestamp");
        ImGui.TableSetupColumn("Entity ID");
        ImGui.TableSetupColumn("Name");
        ImGui.TableSetupColumn("ID");
        ImGui.TableSetupColumn("Duration");
        ImGui.TableSetupColumn("Stacks");
        ImGui.TableSetupColumn("Actions");
        ImGui.TableHeadersRow();
        foreach (var structuredCast in eventTracker.statusEffects)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(structuredCast.Timestamp.ToString("HH:mm:ss"));

            ImGui.TableNextColumn();
            ImGui.Text(structuredCast.EntityId.ToString("x8"));

            ImGui.TableNextColumn();
            var effectName = eventTracker.GetStatusNameById(structuredCast.StatusId);
            ImGui.Text(effectName);

            ImGui.TableNextColumn();
            var castIdStripped = structuredCast.StatusId.ToString("x8").ToUpper().TrimStart('0');
            ImGui.Text(castIdStripped);

            ImGui.TableNextColumn();
            ImGui.Text(structuredCast.Duration.ToString());

            ImGui.TableNextColumn();
            ImGui.Text(structuredCast.Stacks.ToString());

            ImGui.TableNextColumn();
            if (
                ImGui.Button(
                    $"Copy Trigger##{structuredCast.StatusId}-{structuredCast.Timestamp}-{structuredCast.EntityId}"
                )
            )
            {
                CopyToClipboard(
                    $"<?xml version=\"1.0\"?>\n<TriggernometryExport PluginVersion=\"1.2.0.7\">\n"
                        + $"<ExportedTrigger Enabled=\"true\" Source=\"FFXIVNetwork\" Sequential=\"True\" Name=\"{effectName}\" RegularExpression=\"^26\\|[^|]*\\|{castIdStripped}\\|[^|]*\\|(?&lt;debufftimer&gt;[^|]*)\\|[^|]*\\|[^|]*\\|[^|]*\\|(?&lt;name&gt;[^|]*)\\|\">"
                        + $"<Condition Enabled=\"true\" Grouping=\"And\">"
                        + $"<ConditionSingle Enabled=\"true\" ExpressionL=\"${{name}}\" ExpressionTypeL=\"String\" ExpressionR=\"${{_ffxivplayer}}\" ExpressionTypeR=\"String\" ConditionType=\"StringEqualNocase\" />"
                        + $"<ConditionSingle Enabled=\"true\" ExpressionL=\"${{debufftimer}}\" ExpressionTypeL=\"String\" ExpressionR=\"{float.Round(structuredCast.Duration).ToString("F0")}.00\" ExpressionTypeR=\"String\" ConditionType=\"StringEqualNocase\" />"
                        + $"</Condition>"
                        + $"</ExportedTrigger>"
                        + $"</TriggernometryExport>"
                );
            }
        }
        ImGui.EndTable();
    }

    public void DrawActorCasts()
    {
        ImGui.Text("Tracked Actor Casts");
        ImGui.SameLine();
        if (ImGui.Button("Clear Tracked"))
        {
            eventTracker.actorCastsDict.Clear();
        }
        ImGui.BeginTable(
            $"eventtrack",
            5,
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Sortable
        );
        ImGui.TableSetupColumn("NPC Name");
        ImGui.TableSetupColumn("Name ID");
        ImGui.TableSetupColumn("Cast Name");
        ImGui.TableSetupColumn("Cast Id");
        ImGui.TableSetupColumn("Actions");
        ImGui.TableHeadersRow();
        foreach (var structuredCast in eventTracker.actorCastsDict)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            var name = eventTracker.GetBNpcNameById(structuredCast.Value.NameId);
            ImGui.Text(name);

            ImGui.TableNextColumn();
            ImGui.Text(structuredCast.Value.NameId.ToString("x4").ToUpper());

            ImGui.TableNextColumn();
            var cast = eventTracker.GetActionNameById(structuredCast.Value.CastId);
            ImGui.Text(cast);

            ImGui.TableNextColumn();
            var castIdStripped = structuredCast
                .Value.CastId.ToString("x8")
                .ToUpper()
                .TrimStart('0');
            ImGui.Text(castIdStripped);

            ImGui.TableNextColumn();
            if (ImGui.Button($"Copy Trigger##{structuredCast.Key}"))
            {
                var castRegex = $"^20\\|(?:[^|]*\\|){{3}}{castIdStripped}\\|";
                CopyToClipboard(
                    $"<?xml version=\"1.0\"?>"
                        + $"<TriggernometryExport PluginVersion=\"1.2.0.7\">"
                        + $"<ExportedTrigger Enabled=\"true\" Source=\"FFXIVNetwork\" Name=\"{cast} by {name}\" RegularExpression=\"{castRegex}\">"
                        + $"<Condition Enabled=\"false\" Grouping=\"Or\" />"
                        + $"</ExportedTrigger>"
                        + $"</TriggernometryExport>"
                );
            }
        }
        ImGui.EndTable();
    }

    public static void CopyToClipboard(string message) => ImGui.SetClipboardText(message);

    public static void DrawTabs(
        string tabId,
        params (string label, uint color, Action function)[] tabs
    )
    {
        ImGui.BeginTabBar($"##{tabId}");
        foreach (var tab in tabs)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, tab.color);
            if (ImGui.BeginTabItem($"{tab.label}##{tabId}"))
            {
                ImGui.PopStyleColor();
                tab.function();
                ImGui.EndTabItem();
            }
            else
            {
                ImGui.PopStyleColor();
            }
        }

        ImGui.EndTabBar();
    }
}
