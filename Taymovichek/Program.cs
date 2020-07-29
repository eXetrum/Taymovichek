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
using Taymovichek.Modules;

namespace Taymovichek
{
    
    class Program
    {
        static void Main(string[] args) => new Program().RunBotAsync().GetAwaiter().GetResult();

        // Const things
        private static int DELAY = 60 * 1000;                       // 60 sec (milliseconds)
        private static int MAX_SECONDS_BETWEEN_UPDATE = 15 * 60;    // 15 min (seconds)
        private static ulong CHANNEL_ID = 730891176114389150;       // textchannel id
        private static bool IS_PM_AVAILABLE = false;                // Send notifications via PM

        private static string BOT_PREFIX = "!";
        private static string TOKEN = "YOUR_TOKEN_HERE";
        private static string ANNOUNCEMENT_MESSAGE = "АХТУНГ ! ШОТОПРОИЗОШЛО !";

        public static string SERVER_STATUS_URL = @"http://wowcircle.net/stat.html";
        private static string USER_AGENT = @"User-Agent: Mozilla/5.0 (Windows NT 6.1; Win64; x64; rv:78.0) Gecko/20100101 Firefox/78.0";

        private static string CFG_PREFIX = "[Username]:";
        private static string CFG_SEPARATOR = "\r\n";


        private DiscordSocketClient _client;
        private CommandService _commands;
        private IServiceProvider _services;

        // Private data      
        static private Dictionary<string, WowCircleServer> cache = new Dictionary<string, WowCircleServer>();
        static private Dictionary<string, List<string>> watchers = new Dictionary<string, List<string>>();
        static private HashSet<string> globalServerNames = new HashSet<string>();
        static private Dictionary<string, int> notifyCounters = new Dictionary<string, int>();

        static Configuration cfg = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);

        public async Task RunBotAsync()
        {
            // Read resources (prev settings)
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

        public static bool IsServerExists(string serverName)
        {
            return globalServerNames.Contains(serverName);
        }

        private static void EnsureUserExists(string nickName) {
            if (!watchers.ContainsKey(nickName)) 
                watchers.Add(nickName, new List<string>());

            if (cfg.AppSettings.Settings[CFG_PREFIX + nickName] == null)
                cfg.AppSettings.Settings.Add(CFG_PREFIX + nickName, "");
        }

        public static HashSet<string> GetServerNames()
        {
            return new HashSet<string>(globalServerNames);
        }

        public static Dictionary<string, WowCircleServer> GetCache()
        {
            return new Dictionary<string, WowCircleServer>(cache);
        }

        public static List<string> GetUserList(string nickName)
        {
            EnsureUserExists(nickName);
            return new List<string>(watchers[nickName]);
        }

        public static bool IsSubscribeExists(string nickName, string serverName)
        {
            EnsureUserExists(nickName);
            return watchers[nickName].Contains(serverName);
        }

        public static void Unwatch(string nickName, string serverName)
        {
            EnsureUserExists(nickName);

            string value = cfg.AppSettings.Settings[CFG_PREFIX + nickName].Value;
            List<string> servers = value.Split(CFG_SEPARATOR, StringSplitOptions.RemoveEmptyEntries).ToList();
            // Remove from local list
            servers.Remove(serverName);
            // Remove from subs list
            watchers[nickName].Remove(serverName);

            // Refresh cfg 
            cfg.AppSettings.Settings[CFG_PREFIX + nickName].Value = String.Join(CFG_SEPARATOR, servers.ToArray());
            // Save changes
            cfg.Save(ConfigurationSaveMode.Modified);
            ConfigurationManager.RefreshSection("appSettings");
        }

        public static void Watch(string nickName, string serverName)
        {
            EnsureUserExists(nickName);

            if (watchers[nickName].Contains(serverName)) return;
            
            string value = cfg.AppSettings.Settings[CFG_PREFIX + nickName].Value;
            List<string> servers = value.Split(CFG_SEPARATOR, StringSplitOptions.RemoveEmptyEntries).ToList();
            
            // Add to local list
            if(!servers.Contains(serverName))
                servers.Add(serverName);

            // Add to subs list
            watchers[nickName].Add(serverName);

            // Refresh cfg 
            cfg.AppSettings.Settings[CFG_PREFIX + nickName].Value = String.Join(CFG_SEPARATOR, servers.ToArray());
            // Save changes
            cfg.Save(ConfigurationSaveMode.Modified);
            ConfigurationManager.RefreshSection("appSettings");
        }

        private void LoadResources()
        {
            foreach (var key in cfg.AppSettings.Settings.AllKeys)
            {
                if(key.StartsWith(CFG_PREFIX))
                {
                    string value = cfg.AppSettings.Settings[key].Value;
                    string username = key.Substring(CFG_PREFIX.Length);
                    EnsureUserExists(username);

                    List<string> servers = new List<string>(value.Split(CFG_SEPARATOR, StringSplitOptions.RemoveEmptyEntries).ToArray());
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


        private Task OnClientReady()
        {
            Thread th = new Thread(FetchStatus);
            th.IsBackground = true;
            th.Start();
            return Task.CompletedTask;
        }

        private void FetchStatus(object obj)
        {
            while (true)
            {
                try
                {
                    // Update servers data
                    updateServers();

                    // Notify subscribers
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
            HtmlWeb web = new HtmlWeb();
            web.UserAgent = USER_AGENT;
            web.UseCookies = true;
            web.UsingCache = false;
            web.UsingCacheIfExists = false;
            web.CacheOnly = false;
            //web.CaptureRedirect = true;            

            HtmlDocument doc = web.Load(SERVER_STATUS_URL);

            HtmlNode[] nodes = doc.DocumentNode.SelectNodes("//div[contains(@class, 'server-item-bg')]").ToArray();

            string message = "";
            foreach (HtmlNode item in nodes)
            {
                string serverName = item.SelectSingleNode(".//div[contains(@class, 'name')]").InnerText;
                string onlinestr = item.SelectSingleNode(".//div[contains(@class, 'online')]").InnerText;
                string uptimestr = item.SelectSingleNode(".//div[contains(@class, 'time')]").InnerText;

                int online = Int32.Parse(onlinestr.Replace("Онлайн:", ""));
                TimeSpan uptime = parseUptime(uptimestr);

                if (!globalServerNames.Contains(serverName)) globalServerNames.Add(serverName);
                if (!notifyCounters.ContainsKey(serverName)) notifyCounters.Add(serverName, 0);

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
                    // Reset notification counter for current server
                    notifyCounters[serverName] = 0;
                }
            }
            Console.WriteLine("=================[Cache timestamp: {0}]=================", DateTime.Now);
        }

        private void notifySubscribers()
        {
            Queue<DiscordNotificationEntity> notifications = new Queue<DiscordNotificationEntity>();

            var channelSocket = _client.GetChannel(CHANNEL_ID) as IMessageChannel;

            List<string> brokenServers = new List<string>();

            foreach (var serverName in globalServerNames)
            {
                if (cache[serverName].Status == WowCircleServer.StatusEnum.DOWN)
                {
                    if (!brokenServers.Contains(serverName)) brokenServers.Add(serverName);

                    // Search subscribers 
                    foreach (var userName in watchers.Keys)
                    {
                        if (watchers[userName].Contains(serverName))
                        {
                            // Send private message notification
                            if (IS_PM_AVAILABLE && notifyCounters[serverName] == 0)
                            {
                                string message = string.Format("{0} ->>>> {1}\n", ANNOUNCEMENT_MESSAGE, serverName);
                                notifications.Enqueue(new DiscordPMNotificationEntity(_client, message, userName));
                            }
                        }
                    }
                }
            }

            // Make text channel notifications
            foreach(var serverName in brokenServers)
            {
                if (notifyCounters[serverName] == 0)
                {
                    string message = string.Format("{0} ->>>> {1}\n", ANNOUNCEMENT_MESSAGE, serverName);
                    notifications.Enqueue(new DiscordTextChannelNotificationEntity(_client, message, CHANNEL_ID));
                    notifyCounters[serverName]++;
                }
            }

            processNotifications(notifications);
        }

        private static void processNotifications(Queue<DiscordNotificationEntity> notifications)
        {
            const int MAX_ATTEMPT = 3;
            
            while(notifications.Count > 0)
            {
                DiscordNotificationEntity entity = notifications.Dequeue();
                for (int attempt = 0; attempt < MAX_ATTEMPT; ++attempt)
                {
                    try
                    {
                        entity.SendNotification();
                        break;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Error Sending Notification: {0} ||| {1}{2}", ex.Message, ex.StackTrace, Environment.NewLine);
                    }
                    Thread.Sleep(1500);
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
            if(message.HasStringPrefix(BOT_PREFIX, ref argPos))
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
