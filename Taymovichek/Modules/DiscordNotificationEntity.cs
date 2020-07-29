using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Text;

namespace Taymovichek.Modules
{
    public abstract class DiscordNotificationEntity
    {
        protected DiscordSocketClient client;
        protected string message;
        protected DiscordNotificationEntity(DiscordSocketClient client, string message)
        {
            this.client = client;
            this.message = message;
        }
        public abstract void SendNotification();
    }
}
