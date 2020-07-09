﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;

using Microsoft.Extensions.DependencyInjection;

using Discord;
using Discord.Commands;
using Discord.WebSocket;
using System.Threading;

using HtmlAgilityPack;
using System.IO;
using System.Text.RegularExpressions;

namespace Taymovichek
{
    class CircleServer
    {
        public enum StatusEnum { UP, DOWN }
        public string Name { get; set; }
        public int Online { get; set; }
        public DateTime OnlineLastModified { get; set; }
        public TimeSpan Uptime { get; set; }
        public DateTime UptimeLastModified { get; set; }

        public StatusEnum Status { get; set; }
    }
    class Program
    {
        static void Main(string[] args) => new Program().RunBotAsync().GetAwaiter().GetResult();

        // Const things
        private int DELAY = 30 * 1000; // 30 sec (milliseconds)
        private int MAX_SECONDS_BETWEEN_UPDATE = 10 * 60; // 10 min (seconds)


        private string TOKEN = "NzMwODkxNjU0ODY4ODkzNzU3.XweGNQ.pLk_xVjYKqy2yolGjzkZ5jCgpEI";
        private string ANNOUNCEMENT_MESSAGE = "АХТУНГ ! ШОТОПРОИЗОШЛО !";

        private string URL = @"http://wowcircle.net/stat.html";
        private string USER_AGENT = @"User-Agent: Mozilla/5.0 (Windows NT 6.1; Win64; x64; rv:78.0) Gecko/20100101 Firefox/78.0";

        private DiscordSocketClient _client;
        private CommandService _commands;
        private IServiceProvider _services;
        private ulong botID;

        // Public data        
        static public Dictionary<string, CircleServer> cache = new Dictionary<string, CircleServer>();
        static public Dictionary<string, List<string>> watchers = new Dictionary<string, List<string>>();
        static public HashSet<string> servers = new HashSet<string>();


        public async Task RunBotAsync()
        {
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

        private Task OnClientReady()
        {
            Thread th = new Thread(FetchStatus);
            th.IsBackground = true;
            th.Start();
            return Task.CompletedTask;
        }

        private void FetchStatus(object obj)
        {
            botID = _client.CurrentUser.Id;

            while (true)
            {
                try
                {

                    HtmlWeb web = new HtmlWeb();
                    web.UserAgent = USER_AGENT;
                    web.UseCookies = true;
                    web.UsingCache = false;
                    //web.CaptureRedirect = true;

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
                                new CircleServer
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
                            cache[serverName].Status = CircleServer.StatusEnum.DOWN;
                        } 
                        else
                        {
                            cache[serverName].Status = CircleServer.StatusEnum.UP;
                        }
                    }
                    Console.WriteLine();

                    // Check subscribers
                    foreach(var userName in watchers.Keys)
                    {
                        foreach(var server in watchers[userName])
                        {
                            if(cache[server].Status == CircleServer.StatusEnum.DOWN)
                            {
                                string[] chunks = userName.Split('#');
                                string username = chunks[0];
                                string discriminator = chunks[1];

                                SocketUser userSocket = _client.GetUser(username, discriminator);
                                userSocket.SendMessageAsync(string.Format("{0} ->>>> {1}\n", ANNOUNCEMENT_MESSAGE, server));
                            }
                        }
                    }

                    
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("Error: {0}", ex.Message);
                }
                

                Thread.Sleep(DELAY);
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