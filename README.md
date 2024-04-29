# StatusSwitcher
A simple Discord bot for plural systems, that allows one to register a front switch within the PluralKit API by simply changing an emote in their custom status.

## Setup
Clone the repo, build it as any other .NET 8 project, fill out the config and put a path to it in the `CONFIG_PATH` env-var, then run.
In your PluralKit dashboard, add `[status-switch-emote=<AN_EMOTE>]` to every member's description that you want tracked.

To hot reload your member list, type `.ssreload` in any channel visible to the bot.