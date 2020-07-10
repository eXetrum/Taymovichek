using Discord;
using Discord.Commands;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Taymovichek.Modules
{
    public class Commands : ModuleBase<SocketCommandContext>
    {
        [Command("help")]
        public async Task Help ()
        {
            string[] commands = { "help", "list", "watch", "unwatch", "status" };

            StringBuilder sb = new StringBuilder();
            foreach (var item in commands) sb.Append(item + "\n");

            await ReplyAsync(sb.ToString());
        }

        [Command("list")]
        public async Task ListServers()
        {
            string nickname = Context.User.Username + "#" + Context.User.Discriminator;

            // Ensure nickname exists
            if (!Program.watchers.ContainsKey(nickname)) Program.watchers.Add(nickname, new List<string>());


            StringBuilder sb = new StringBuilder();
            if (Program.watchers[nickname].Count == 0)
            {
                sb.Append("Your watching list is empy.\n");
            }
            else
            {
                foreach (var serverName in Program.watchers[nickname])
                {
                    sb.Append(string.Format("{0}\n", serverName));
                }
            }
            await ReplyAsync(sb.ToString());
        }

        [Command("unwatch")]
        public async Task UnWatch(string serverName)
        {
            string nickname = Context.User.Username + "#" + Context.User.Discriminator;

            // Ensure nickname exists
            if (!Program.watchers.ContainsKey(nickname)) Program.watchers.Add(nickname, new List<string>());

            if (serverName.Equals("*"))
            {
                foreach (var server in Program.servers)
                {
                    Program.watchers[nickname].Remove(server);
                    Program.RemoveServer(nickname, server);
                }
                await ReplyAsync(string.Format("{0} now is no more watching for any server\n", Context.User.Username));
                return;
            }

            // Ensure server name is valid name
            if (!Program.servers.Contains(serverName))
            {
                await ReplyAsync(string.Format("{0} is incorrect server name\n", serverName));
                return;
            }

            if (Program.watchers[nickname].Contains(serverName))
            {
                Program.watchers[nickname].Remove(serverName);
                Program.RemoveServer(nickname, serverName);
                await ReplyAsync(string.Format("{0} is no more watching for {1}\n", Context.User.Username, serverName));
                return;
            }
            await ReplyAsync(string.Format("{0} is not watching yet for {1}\n", Context.User.Username, serverName));
        }

        [Command("watch")]
        public async Task Watch(string serverName)
        {
            string nickname = Context.User.Username + "#" + Context.User.Discriminator;

            // Ensure nickname exists
            if (!Program.watchers.ContainsKey(nickname))  Program.watchers.Add(nickname, new List<string>());
            
            if(serverName.Equals("*"))
            {
                foreach(var server in Program.servers)
                {
                    if (!Program.watchers[nickname].Contains(server))
                    {
                        Program.watchers[nickname].Add(server);
                        Program.AddServer(nickname, server);
                    }
                }
                await ReplyAsync(string.Format("{0} now is watching for each server\n", Context.User.Username));
                return;
            }

            // Ensure server name is valid name
            if(!Program.servers.Contains(serverName))
            {
                await ReplyAsync(string.Format("{0} is incorrect server name\n", serverName));
                return;
            }

            if (Program.watchers[nickname].Contains(serverName))
            {
                await ReplyAsync(string.Format("{0} already assigned to {1}\n", serverName, Context.User.Username));
                return;
            }
            Program.watchers[nickname].Add(serverName);
            Program.AddServer(nickname, serverName);
            await ReplyAsync(string.Format("{0} now is watching for {1}\n", Context.User.Username, serverName));
        }

        [Command("status")]
        public async Task Status()
        {
            string DISCORD_FORMAT_STR = "{0,-20}\n{1,-16}\n{2,-16}\n";
            string CONSOLE_FORMAT_STR = "{0,-20}{1,-16}{2,-16}{3,-12}\n";

            string title = "WowCircle Server Status";
            var fields = new List<EmbedFieldBuilder>();

            StringBuilder sb = new StringBuilder();
            sb.Append(string.Format(CONSOLE_FORMAT_STR, "ServerName", "Online", "Uptime", "Status"));
            foreach (var item in Program.cache)
            {
                string consoleText = string.Format(CONSOLE_FORMAT_STR,
                    item.Value.Name,
                    item.Value.Online,
                    item.Value.Uptime,
                    item.Value.Status.ToString());

                string discordText = string.Format(DISCORD_FORMAT_STR,
                    item.Value.Online,
                    item.Value.Uptime,
                    item.Value.Status.ToString());

                sb.Append(consoleText);

                fields.Add(new EmbedFieldBuilder() { IsInline = true, Name = item.Value.Name, Value = discordText });
            }



            var embed = new EmbedBuilder() {
                Title = title,
                Fields = fields
            };
            

            Console.WriteLine(sb.ToString());
            await ReplyAsync("", false, embed.Build());
        }

    }
}
