# BattleLog Plugin
**A FFXIV Plugin to simplify creating triggers for fights.**
## Current State

There are two tabs:
- Debuffs applied to your player
- Casts by enemies

Each of these tabs has an option that is "Copy Trigger". This option will copy a triggernomotry trigger for that specific debuff or cast.

This is still in very early POC, and is rather unfinished. The original intent for this plugin was much more ambitious but alas I don't have that much desire to fix up the various odds and ends that come with parsing network data.



## Future State
This plugin wants to be a tool to track debuffs, buffs, and action uses during raid encounters in FFXIV.

This is currently in POC phase and has few features. As it is POC, any configuration may or may not be lost when upgrading between versions.

The goal is some of the following:
- Track boss ability timeslines
- Track player debuff timelines
- Ability to create fast triggers for triggernometry (and potentially other things in the future)
   - Templates for common usecases
- Ability to create in-plugin triggers that are cross compatible with triggernometry (Probably never happening)


## Installation
To use the plugin, please install the custom repository with the following link:

`https://raw.githubusercontent.com/KangasZ/DalamudPluginRepository/main/plugin_repository.json`

Please do not include my projects in compilation repositories. This is to ensure the most up-to-date and safe version. If you are using another repository, please swap to this one.

## Notice
This is distributed with the MIT license. I take absolutely no responsibility for any actions by the users of this plugin.

The entire plugin is open source, and users are free to look into the code to see what is going on under the hood.