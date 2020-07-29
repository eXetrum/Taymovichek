using Discord;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Text;

namespace Taymovichek.Modules
{
    public class DiscordTextChannelNotificationEntity : DiscordNotificationEntity
    {
        private ulong channelID;
        public DiscordTextChannelNotificationEntity(DiscordSocketClient client, string message,
            ulong channelID)
            : base(client, message)
        {
            this.channelID = channelID;
        }

        public override void SendNotification()
        {
            if (client == null) throw new Exception("Discord client == null");
            var channelSocket = client.GetChannel(channelID) as IMessageChannel;
            if (channelSocket == null) throw new Exception("ChannelSocket == null");

            channelSocket.SendMessageAsync(message);
        }
    }
}
