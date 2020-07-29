using Discord;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Text;

namespace Taymovichek.Modules
{
    public class DiscordPMNotificationEntity : DiscordNotificationEntity
    {
        private string userName;
        private bool IsPMAvailable;
        public DiscordPMNotificationEntity(DiscordSocketClient client, string message,
            string userName, bool IsPMAvailable = true)
            : base(client, message)
        {
            this.userName = userName;
            this.IsPMAvailable = IsPMAvailable;
        }

        public override void SendNotification()
        {
            if (!IsPMAvailable) return;
            if (client == null) throw new Exception("Discord client == null");

            string[] chunks = userName.Split('#');
            string username = chunks[0];
            string discriminator = chunks[1];

            SocketUser userSocket = client.GetUser(username, discriminator);
            if (userSocket == null) throw new Exception("SocketUser == null");

            userSocket.SendMessageAsync(message);
        }
    }
}
