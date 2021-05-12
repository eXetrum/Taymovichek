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
using System.Collections.Concurrent;

namespace Taymovichek
{
    
    class Program
    {
        static void Main(string[] args) => new Program().RunBotAsync().GetAwaiter().GetResult();

        // Const things
        public static string BASE_URL = @"http://wowcircle.net/stat.html";
        private static string DISCORD_APP_TOKEN = "YOUR_TOKEN_HERE";
        private static ulong CHANNEL_ID = 730891176114389150;           // textchannel id
        private static int FETCH_DELAY = 60;                            // 60 sec 
        private static int UPDATE_DELAY = 15 * 60;                      // 15 min (seconds)

        private static string BOT_PREFIX = "!";
        private static string USER_AGENT = @"User-Agent: Mozilla/5.0 (Windows NT 6.1; Win64; x64; rv:78.0) Gecko/20100101 Firefox/78.0";
        private static string ALLERT_MSG = "АХТУНГ ! ШОТОПРОИЗОШЛО !";


        private static string CFG_PREFIX = "[UserList]:";
        private static string CFG_SEPARATOR = "\r\n";


        private static DiscordSocketClient _client;
        private CommandService _commands;
        private IServiceProvider _services;

        // Private data      
        static private Dictionary<string, WowCircleServer> cache = new Dictionary<string, WowCircleServer>();
        static private List<string> watchingList = new List<string>();
        static private HashSet<string> globalServerNames = new HashSet<string>();
        static private Dictionary<string, int> notifyCounters = new Dictionary<string, int>();

        static private object locker = new object();

        static Configuration cfg = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);

        public static void LoadSettings()
        {
            try { BASE_URL = ConfigurationManager.AppSettings["BASE_URL"]; } catch { }
            try { DISCORD_APP_TOKEN = ConfigurationManager.AppSettings["DISCORD_APP_TOKEN"]; } catch { }
            try { CHANNEL_ID = ulong.Parse(ConfigurationManager.AppSettings["CHANNEL_ID"]); } catch { }
            try { FETCH_DELAY = int.Parse(ConfigurationManager.AppSettings["FETCH_DELAY"]); } catch { }
            try { UPDATE_DELAY = int.Parse(ConfigurationManager.AppSettings["UPDATE_DELAY"]); } catch { }
            try { BOT_PREFIX = ConfigurationManager.AppSettings["BOT_PREFIX"]; } catch { }
            try { USER_AGENT = ConfigurationManager.AppSettings["USER_AGENT"]; } catch { }
            try { ALLERT_MSG = ConfigurationManager.AppSettings["ALLERT_MSG"]; } catch { }
            Console.WriteLine("=>" + FETCH_DELAY);
        }

        public async Task RunBotAsync()
        {
            // Read resources (prev settings)
            LoadResources();

            // Load settings
            LoadSettings();

            _client = new DiscordSocketClient();
            _commands = new CommandService();

            _services = new ServiceCollection()
                .AddSingleton(_client)
                .AddSingleton(_commands)
                .BuildServiceProvider();

            _client.Log += HandleLog;

            _client.Ready += OnClientReady;

            await RegisterCommandsAsync();

            await _client.LoginAsync(TokenType.Bot, DISCORD_APP_TOKEN);

            await _client.StartAsync();

            await Task.Delay(-1);
        }

        public static bool IsServerExists(string serverName)
        {
            return globalServerNames.Contains(serverName);
        }

        public static HashSet<string> GetServerNames()
        {
            lock (locker)
            {
                HashSet<string> clone = new HashSet<string>(globalServerNames);
                return clone;
            }
        }

        public static Dictionary<string, WowCircleServer> GetCache()
        {
            lock (locker)
            {
                Dictionary<string, WowCircleServer> clone = new Dictionary<string, WowCircleServer>(cache);
                return clone;
            }
        }

        public static List<string> GetUserList()
        {
            lock(locker)
            {
                List<string> clone = new List<string>(watchingList);
                return clone;
            }            
        }

        public static bool IsSubscribeExists(string serverName)
        {
            lock (locker)
            {
                return watchingList.Contains(serverName);
            }
        }

        private static void RefreshCfg(List<string> servers)
        {
            // Refresh cfg 
            cfg.AppSettings.Settings[CFG_PREFIX].Value = String.Join(CFG_SEPARATOR, servers.ToArray());
            // Save changes
            cfg.Save(ConfigurationSaveMode.Modified);
            ConfigurationManager.RefreshSection("appSettings");
        }

        public static void Unwatch(string serverName)
        {
            string value = cfg.AppSettings.Settings[CFG_PREFIX].Value;
            List<string> servers = value.Split(CFG_SEPARATOR, StringSplitOptions.RemoveEmptyEntries).ToList();
            // Remove from local list
            servers.Remove(serverName);
            // Remove from subs list
            watchingList.Remove(serverName);

            RefreshCfg(servers);
        }

        public static void Watch(string serverName)
        {
            if (watchingList.Contains(serverName)) return;
            
            string value = cfg.AppSettings.Settings[CFG_PREFIX].Value;
            List<string> servers = value.Split(CFG_SEPARATOR, StringSplitOptions.RemoveEmptyEntries).ToList();
            
            // Add to local list
            if(!servers.Contains(serverName))
                servers.Add(serverName);

            // Add to subs list
            watchingList.Add(serverName);

            RefreshCfg(servers);
        }

        private static void LoadResources()
        {
            if (cfg.AppSettings.Settings[CFG_PREFIX] == null)
                cfg.AppSettings.Settings.Add(CFG_PREFIX, "");

            foreach (var key in cfg.AppSettings.Settings.AllKeys)
            {
                Console.WriteLine("KEY={0}, Value=\n{1}\n\n\n", key, cfg.AppSettings.Settings[key].Value);
                if(key.Equals(CFG_PREFIX))
                {
                    string value = cfg.AppSettings.Settings[key].Value;

                    List<string> servers = new List<string>(value.Split(CFG_SEPARATOR, StringSplitOptions.RemoveEmptyEntries).ToArray());
                    foreach (var serverName in servers)
                    {
                        if (!watchingList.Contains(serverName))
                        {
                            watchingList.Add(serverName);
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

                int sleepTimeout = new Random().Next(FETCH_DELAY, 2 * FETCH_DELAY) * 1000;
                Thread.Sleep(sleepTimeout);
            }
        }

        private static void updateServers()
        {
            try
            {
                HtmlWeb web = new HtmlWeb();
                web.UserAgent = USER_AGENT;
                web.UseCookies = true;
                web.UsingCache = false;
                web.UsingCacheIfExists = false;
                web.CacheOnly = false;
                //web.CaptureRedirect = true;            

                HtmlDocument doc = web.Load(BASE_URL);

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
                        || secondsBetweenUpdate >= UPDATE_DELAY)
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
            catch(Exception ex)
            {
                Console.WriteLine("updateServers failure: {0} ||| {1}{2}", ex.Message, ex.StackTrace, Environment.NewLine);
            }
        }

        private static void notifySubscribers()
        {
            Queue<DiscordNotificationEntity> notifications = new Queue<DiscordNotificationEntity>();

            List<string> brokenServers = new List<string>();

            foreach (var serverName in globalServerNames)
            {
                if (cache[serverName].Status == WowCircleServer.StatusEnum.DOWN 
                    && watchingList.Contains(serverName) 
                    && notifyCounters[serverName] == 0)
                {
                    string message = string.Format("{0} ->>>> {1}\n", ALLERT_MSG, serverName);
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
                    Thread.Sleep(1);
                }
            }
        }

        public static string EnquoteStatus(WowCircleServer.StatusEnum status)
        {
            if (status == WowCircleServer.StatusEnum.UP)
                return "```diff\n+" + status.ToString() + "\n```";
            return "```diff\n-" + status.ToString() + "\n```";
        }

        private static TimeSpan parseUptime(string uptime)
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
