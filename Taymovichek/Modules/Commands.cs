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
            string[] commands = { 
                "help - отобразить это меню", 
                "list - показать список слежения", 
                "watch <\"server name\"> - добавить сервер в список слежения", 
                "unwatch <\"server name\"> - убрать сервер из списка слежения", 
                "status - отобразить текущий статус серверов" 
            };

            StringBuilder sb = new StringBuilder();
            foreach (var item in commands) sb.Append(item + Environment.NewLine);

            await ReplyAsync(sb.ToString());
        }

        [Command("list")]
        public async Task ListServers()
        {
            string nickName = string.Format("{0}#{1}", Context.User.Username, Context.User.Discriminator);
            var userList = Program.GetUserList(nickName);

            if (userList.Count == 0)
            {
                await ReplyAsync(string.Format("Ваш список слежения пуст.{0}", Environment.NewLine));
                return;
            }

            StringBuilder sb = new StringBuilder();
            foreach (var serverName in userList)
            {
                sb.Append(string.Format("{0}{1}", serverName, Environment.NewLine));
            }

            await ReplyAsync(sb.ToString());
            return;
        }

        [Command("unwatch")]
        public async Task UnWatch(string serverName)
        {
            string nickName = string.Format("{0}#{1}", Context.User.Username, Context.User.Discriminator);

            var userList = Program.GetUserList(nickName);

            if(userList.Count == 0)
            {
                await ReplyAsync(string.Format("Ваш список слежения пуст.{0}", Environment.NewLine));
                return;
            }

            // Unwatch everything
            if (serverName.Equals("*"))
            {
                foreach (var server in userList)
                {
                    Program.Unwatch(nickName, server);
                }

                await ReplyAsync(string.Format("{0} более не следит ни за одним сервером{1}", Context.User.Username, Environment.NewLine));
                return;
            }

            // Ensure server name is valid name
            if (!Program.IsServerExists(serverName))
            {
                await ReplyAsync(string.Format("{0} некорректное имя сервера{1}", serverName, Environment.NewLine));
                return;
            }

            if(!Program.IsSubscribeExists(nickName, serverName))
            {
                await ReplyAsync(string.Format("{0} еще не следит за сервером {1}{2}", Context.User.Username, serverName, Environment.NewLine));
                return;
            }

            Program.Unwatch(nickName, serverName);
            await ReplyAsync(string.Format("{0} более не следит за сервером {1}{2}", Context.User.Username, serverName, Environment.NewLine));
        }

        [Command("watch")]
        public async Task Watch(string serverName)
        {
            string nickName = string.Format("{0}#{1}", Context.User.Username, Context.User.Discriminator);

            // Watch for everything
            if (serverName.Equals("*"))
            {
                foreach (var server in Program.GetServerNames())
                {
                    Program.Watch(nickName, server);
                }

                await ReplyAsync(string.Format("{0} следит за всеми серверами.{1}", Context.User.Username, Environment.NewLine));
                return;
            }

            // Ensure server name is valid name
            if(!Program.IsServerExists(serverName))
            {
                await ReplyAsync(string.Format("{0} некорректное имя сервера.{1}", serverName, Environment.NewLine));
                return;
            }

            if (Program.IsSubscribeExists(nickName, serverName))
            {
                await ReplyAsync(string.Format("{0} уже следит за сервером {1}{2}", serverName, Context.User.Username, Environment.NewLine));
                return;
            }

            Program.Watch(nickName, serverName);
            await ReplyAsync(string.Format("{0} теперь следит за сервером {1}{2}", Context.User.Username, serverName, Environment.NewLine));
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
            foreach (var item in Program.GetCache())
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
                Url = Program.SERVER_STATUS_URL,
                Fields = fields
            };
  
            Console.WriteLine(sb.ToString());
            await ReplyAsync("", false, embed.Build());
        }

    }
}
