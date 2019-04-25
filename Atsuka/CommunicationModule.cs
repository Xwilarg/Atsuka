using Discord;
using Discord.Commands;
using DiscordUtils;
using System;
using System.Threading.Tasks;

namespace Atsuka
{
    public class CommunicationModule : ModuleBase
    {
        [Command("Info")]
        private async Task Info()
        {
            await ReplyAsync("", false, Utils.GetBotInfo(Program.P.StartTime, "Atsuki", Program.P.client.CurrentUser));
        }

        [Command("Help")]
        private async Task Help()
        {
            await ReplyAsync("", false, new EmbedBuilder()
            {
                Color = Color.Purple,
                Title = "Help",
                Description =
                    "I'm here to make sure everyone stay polite and civilized." + Environment.NewLine +
                    "If you want more information about me, you can do the 'info' command"
            }.Build());
        }
    }
}
