using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;

// HUI ZNAET SHO nu tipo shobi service zainjektit
using Microsoft.Extensions.DependencyInjection;

// Discord API lib
using Discord;
using Discord.Commands;
using Discord.WebSocket;

// HTML Parser
using HtmlAgilityPack;
using System.Configuration;
using SuperSocket.ClientEngine;
using System.Text;

namespace Taymovichek
{
    
    class Program
    {
        static void Main(string[] args) => new Program().RunBotAsync().GetAwaiter().GetResult();

        // Const things
        private int DELAY = 60 * 1000; // 60 sec (milliseconds)
        private int MAX_SECONDS_BETWEEN_UPDATE = 15 * 60; // 15 min (seconds)
        private static ulong CHANNEL_ID = 730891176114389150;
        private static bool IS_PM_AVAILABLE = false;

        private string TOKEN = "YOUR_TOKEN_HERE";
        private string ANNOUNCEMENT_MESSAGE = "АХТУНГ ! ШОТОПРОИЗОШЛО !";

        private string URL = @"http://wowcircle.net/stat.html";
        private string USER_AGENT = @"User-Agent: Mozilla/5.0 (Windows NT 6.1; Win64; x64; rv:78.0) Gecko/20100101 Firefox/78.0";
        

        private DiscordSocketClient _client;
        private CommandService _commands;
        private IServiceProvider _services;

        // Public data        
        static public Dictionary<string, WowCircleServer> cache = new Dictionary<string, WowCircleServer>();
        static public Dictionary<string, List<string>> watchers = new Dictionary<string, List<string>>();
        static public HashSet<string> servers = new HashSet<string>();
        static Configuration cfg = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);

        public async Task RunBotAsync()
        {
            // Read resources prev settings
            LoadResources();

            _client = new DiscordSocketClient();
            _commands = new CommandService();

            _services = new ServiceCollection()
                .AddSingleton(_client)
                .AddSingleton(_commands)
                .BuildServiceProvider();

            _client.Log += HandleLog;

            _client.Ready += OnClientReady;

            await RegisterCommandsAsync();

            await _client.LoginAsync(TokenType.Bot, TOKEN);

            await _client.StartAsync();

            await Task.Delay(-1);
        }

        private void LoadResources()
        {
            string PREFIX = "[Username]:";
            string SEPARATOR = "\r\n";
            foreach (var key in cfg.AppSettings.Settings.AllKeys)
            {
                if(key.StartsWith(PREFIX))
                {
                    string value = cfg.AppSettings.Settings[key].Value;
                    string username = key.Substring(PREFIX.Length);
                    if (!watchers.ContainsKey(username))
                    {
                        watchers.Add(username, new List<string>());
                    }

                    string[] servers = value.Split(SEPARATOR, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var serverName in servers)
                    {
                        if (!watchers[username].Contains(serverName))
                        {
                            watchers[username].Add(serverName);
                        }
                    }      
                }
            }
        }

        public static void AddServer(string username, string servername)
        {
            string PREFIX = "[Username]:";
            string SEPARATOR = "\r\n";

            if (cfg.AppSettings.Settings[PREFIX + username] == null)
                cfg.AppSettings.Settings.Add(PREFIX + username, "");

            string value = cfg.AppSettings.Settings[PREFIX + username].Value;
            string[] servers = value.Split(SEPARATOR, StringSplitOptions.RemoveEmptyEntries);

            bool found = false;
            StringBuilder sb = new StringBuilder();
            foreach(var server in servers)
            {
                sb.Append(server + SEPARATOR);
                if (server.Equals(servername)) found = true;
            }

            if (!found) sb.Append(servername + SEPARATOR);

            cfg.AppSettings.Settings[PREFIX + username].Value = sb.ToString();
            cfg.Save(ConfigurationSaveMode.Modified);
            ConfigurationManager.RefreshSection("appSettings");
        }
        public static void RemoveServer(string username, string servername)
        {
            string PREFIX = "[Username]:";
            string SEPARATOR = "\r\n";

            if (cfg.AppSettings.Settings[PREFIX + username] == null)
                cfg.AppSettings.Settings.Add(PREFIX + username, "");

            string value = cfg.AppSettings.Settings[PREFIX + username].Value;
            string[] servers = value.Split(SEPARATOR, StringSplitOptions.RemoveEmptyEntries);

            StringBuilder sb = new StringBuilder();
            foreach (var server in servers)
            {
                if (server.Equals(servername)) continue;
                sb.Append(server + SEPARATOR);
            }

            cfg.AppSettings.Settings[PREFIX + username].Value = sb.ToString();
            cfg.Save(ConfigurationSaveMode.Modified);
            ConfigurationManager.RefreshSection("appSettings");
        }

        private Task OnClientReady()
        {
            Thread th = new Thread(FetchStatus);
            th.IsBackground = true;
            th.Start();
            return Task.CompletedTask;
        }

        private void FetchStatus(object obj)
        {
            HtmlWeb web = new HtmlWeb();
            web.UserAgent = USER_AGENT;
            web.UseCookies = true;
            web.UsingCache = false;
            //web.CaptureRedirect = true;

            while (true)
            {
                try
                {
                    web.UseCookies = true;
                    web.CacheOnly = false;
                    web.UsingCache = false;
                    web.UsingCacheIfExists = false;

                    HtmlDocument doc = web.Load(URL);

                    HtmlNode[] nodes = doc.DocumentNode.SelectNodes("//div[contains(@class, 'server-item-bg')]").ToArray();

                    string message = "";
                    foreach (HtmlNode item in nodes)
                    {
                        string serverName = item.SelectSingleNode(".//div[contains(@class, 'name')]").InnerText;
                        string onlinestr = item.SelectSingleNode(".//div[contains(@class, 'online')]").InnerText;
                        string uptimestr = item.SelectSingleNode(".//div[contains(@class, 'time')]").InnerText;

                        int online = Int32.Parse(onlinestr.Replace("Онлайн:", ""));
                        TimeSpan uptime = parseUptime(uptimestr);

                        if (!servers.Contains(serverName)) servers.Add(serverName);

                        if (!cache.ContainsKey(serverName))
                        {
                            cache.Add(serverName,
                                new WowCircleServer
                                {
                                    Name = serverName,
                                    Online = online,
                                    OnlineLastModified = DateTime.Now,
                                    Uptime = uptime,
                                    UptimeLastModified = DateTime.Now,
                                });
                        }
                        else
                        {
                            if (cache[serverName].Online != online)
                            {
                                cache[serverName].Online = online;
                                cache[serverName].OnlineLastModified = DateTime.Now;
                            }

                            if (cache[serverName].Uptime != uptime)
                            {
                                cache[serverName].Uptime = uptime;
                                cache[serverName].UptimeLastModified = DateTime.Now;
                            }
                        }

                        message += string.Format("{0} | {1} | {2}", serverName, online, uptime) + Environment.NewLine;
                    }

                    // Update servers status
                    updateServers();

                    // Check subscribers
                    notifySubscribers();

                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("Error: {0}", ex.Message);
                }

                int sleepTimeout = new Random().Next(DELAY, 2 * DELAY);
                Thread.Sleep(sleepTimeout);
            }
        }

        private void updateServers()
        {
            Console.WriteLine("{0,-20}  {1,18}  {2, 16}", "Server", "LastModified", "Status");
            foreach (var serverName in cache.Keys)
            {
                long secondsBetweenUpdate = Convert.ToInt64(-1 * cache[serverName].UptimeLastModified.Subtract(DateTime.Now).TotalSeconds);

                Console.WriteLine("{0,-20}  {1,18}  {2, 16}",
                    serverName, secondsBetweenUpdate, cache[serverName].Status.ToString());
                // Uptime == 0 
                // Stats last modf >= 10 min
                if (cache[serverName].Uptime.Equals(TimeSpan.FromSeconds(0))
                    || secondsBetweenUpdate >= MAX_SECONDS_BETWEEN_UPDATE)
                {
                    cache[serverName].Status = WowCircleServer.StatusEnum.DOWN;
                }
                else
                {
                    cache[serverName].Status = WowCircleServer.StatusEnum.UP;
                }
            }
            Console.WriteLine();
        }

        private void notifySubscribers()
        {
            var channelSocket = _client.GetChannel(CHANNEL_ID) as IMessageChannel;

            foreach (var userName in watchers.Keys)
            {
                foreach (var server in watchers[userName])
                {
                    if (cache[server].Status == WowCircleServer.StatusEnum.DOWN)
                    {
                        if (IS_PM_AVAILABLE)
                        {
                            string[] chunks = userName.Split('#');
                            string username = chunks[0];
                            string discriminator = chunks[1];

                            SocketUser userSocket = _client.GetUser(username, discriminator);
                            userSocket.SendMessageAsync(string.Format("{0} ->>>> {1}\n", ANNOUNCEMENT_MESSAGE, server));
                        }

                        channelSocket.SendMessageAsync(string.Format("{0} ->>>> {1}\n", ANNOUNCEMENT_MESSAGE, server));
                    }
                }
            }
        }

        private TimeSpan parseUptime(string uptime)
        {
            uptime = uptime.Replace("д.", "");
            uptime = uptime.Replace("ч.", "");
            uptime = uptime.Replace("м.", "");
            uptime = uptime.Replace("с.", "");
            uptime = uptime.Trim();
            //"{0} д. {1} ч. {2} м. {3} с.".
            string[] chunks = uptime.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            
            int days = 0, hours = 0, minutes = 0, seconds = 0;
            if(chunks.Length == 4)
            {
                Int32.TryParse(chunks[0], out days);
                Int32.TryParse(chunks[1], out hours);
                Int32.TryParse(chunks[2], out minutes);
                Int32.TryParse(chunks[3], out seconds);
            }

            return TimeSpan.FromSeconds(days * 24 * 60 * 60 + hours * 60 * 60 + minutes * 60 + seconds);
        }

        private Task HandleLog(LogMessage arg)
        {
            Console.WriteLine(arg);
            return Task.CompletedTask;
        }

        public async Task RegisterCommandsAsync()
        {
            _client.MessageReceived += HandleCommandAsync;
            await _commands.AddModulesAsync(Assembly.GetEntryAssembly(), _services);
        }

        private async Task HandleCommandAsync(SocketMessage arg)
        {
            var message = arg as SocketUserMessage;
            var ctx = new SocketCommandContext(_client, message);

            // Do nothing for self messages
            if (message.Author.IsBot) return;

            int argPos = 0;
            if(message.HasStringPrefix("!", ref argPos))
            {
                var res = await _commands.ExecuteAsync(ctx, argPos, _services);
                if (!res.IsSuccess)
                {
                    Console.WriteLine(res.ErrorReason);
                    await ctx.Channel.SendMessageAsync(res.ErrorReason);

                }
            }
        }
    }
}
