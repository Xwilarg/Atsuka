using Discord;
using Discord.Commands;
using Discord.WebSocket;
using DiscordUtils;
using Google.Cloud.Translation.V2;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Atsuka
{
    public class Program
    {
        public static async Task Main()
            => await new Program().MainAsync();

        public readonly DiscordSocketClient client;
        private readonly CommandService commands = new CommandService();

        public DateTime StartTime { private set; get; }
        public static Program P { private set; get; }

        private string perspectiveApi;
        private TranslationClient translationClient;

        private List<ulong> bannedIds;
        private ulong reportGuildId;
        private ulong reportChanId;
        private ITextChannel reportChan;

        private static readonly Tuple<string, float>[] categories = new Tuple<string, float>[] {
            new Tuple<string, float>("SEVERE_TOXICITY", .80f),
            new Tuple<string, float>("IDENTITY_ATTACK", .80f),
            new Tuple<string, float>("INSULT", .80f),
            new Tuple<string, float>("THREAT", .80f),
            new Tuple<string, float>("OBSCENE", .90f)
        };

        private Program()
        {
            P = this;
            client = new DiscordSocketClient(new DiscordSocketConfig
            {
                LogLevel = LogSeverity.Verbose,
            });
            client.Log += Utils.Log;
            commands.Log += Utils.LogError;

            bannedIds = new List<ulong>();
        }

        private async Task MainAsync()
        {
            client.MessageReceived += HandleCommandAsync;

            await commands.AddModuleAsync<CommunicationModule>(null);

            dynamic json = JsonConvert.DeserializeObject(File.ReadAllText("Keys/keys.json"));
            Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", (string)json.googleAPIJson);
            translationClient = TranslationClient.Create();
            perspectiveApi = json.perspectiveToken;

            reportChan = null;
            if (json.reportChan != null && json.guildId != null)
            {
                reportChanId = ulong.Parse((string)json.reportChan);
                reportGuildId = ulong.Parse((string)json.guildId);
            }
            else
            {
                reportChanId = 0;
            }

            await client.LoginAsync(TokenType.Bot, (string)json.token);
            StartTime = DateTime.Now;
            await client.StartAsync();

            await Task.Delay(-1);
        }

        private async Task HandleCommandAsync(SocketMessage arg)
        {
            SocketUserMessage msg = arg as SocketUserMessage;
            if (msg == null || arg.Author.IsBot) return;
            int pos = 0;
            if (msg.Channel as ITextChannel == null)
            {
                if (msg.Content.Length > 0 && reportChanId != 0)
                {
                    if (reportChan == null)
                        reportChan = client.GetGuild(reportGuildId).GetTextChannel(reportChanId);
                    await reportChan.SendMessageAsync("", false, new EmbedBuilder()
                    {
                        Title = "I received a private message from " + arg.Author.ToString(),
                        Description = msg.Content,
                        Color = Color.Purple
                    }.Build());
                    await msg.Channel.SendMessageAsync("The message you sent me was reported in " + reportChan.Guild.ToString());
                }
            }
            else if (bannedIds.Contains(msg.Author.Id))
            {
                if (msg.Content == "I apologize for my bad manners, I promize that I won't do it again")
                {
                    bannedIds.Remove(msg.Author.Id);
                    await msg.Channel.SendMessageAsync("I'm glad you understand.");
                }
                else if (await CheckMessage(msg.Content, msg.Author.ToString()))
                {
                    try
                    {
                        await ((IGuildUser)msg.Author).KickAsync("Speaking rudely");
                        await msg.Author.SendMessageAsync("It's look like kicking you is the only effective solution." + Environment.NewLine +
                            "You can come back when you'll be calmed");
                        bannedIds.Remove(msg.Author.Id);
                    }
                    catch (Exception)
                    { } // Bot don't have permissions to kick user
                    await msg.DeleteAsync();
                }
                else
                    await msg.DeleteAsync();
            }
            else if (await CheckMessage(msg.Content, msg.Author.ToString()) && (await ((ITextChannel)msg.Channel).Guild.GetCurrentUserAsync()).GuildPermissions.ManageMessages)
            {
                bannedIds.Add(arg.Author.Id);
                await msg.Channel.SendMessageAsync("How rude of you, didn't we learn you to speak politely ?" + Environment.NewLine +
                    "I won't allow you to say anything until you write \"I apologize for my bad manners, I promize that I won't do it again\".");
            }
            else if (msg.HasMentionPrefix(client.CurrentUser, ref pos) || msg.HasStringPrefix("a.", ref pos))
            {
                SocketCommandContext context = new SocketCommandContext(client, msg);
                await commands.ExecuteAsync(context, pos, null);
            }
        }

        private async Task<bool> CheckMessage(string content, string username)
        {
            if (content.Length == 0)
                return false;
            string finalMsg = (await translationClient.TranslateTextAsync(content, "en")).TranslatedText;
            using (HttpClient hc = new HttpClient())
            {
                HttpResponseMessage post = await hc.PostAsync("https://commentanalyzer.googleapis.com/v1alpha1/comments:analyze?key=" + Program.P.perspectiveApi, new StringContent(
                        JsonConvert.DeserializeObject("{comment: {text: \"" + Utils.EscapeString(finalMsg) + "\"},"
                                                    + "languages: [\"en\"],"
                                                    + "requestedAttributes: {" + string.Join(":{}, ", categories.Select(x => x.Item1)) + ":{}} }").ToString(), Encoding.UTF8, "application/json"));

                dynamic json = JsonConvert.DeserializeObject(await post.Content.ReadAsStringAsync());
                EmbedBuilder embed = new EmbedBuilder()
                {
                    Title = "Identification"
                };
                List<string> flags = new List<string>();
                foreach (var s in categories)
                {
                    double value = json.attributeScores[s.Item1].summaryScore.value;
                    if (value >= s.Item2)
                    {
                        Console.WriteLine(username + " triggered the flag " + s.Item1 + " with a score of " + s.Item2);
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
