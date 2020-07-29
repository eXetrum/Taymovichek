using System;
using System.Collections.Generic;
using System.Text;

namespace Taymovichek
{
    class WowCircleServer
    {
        public enum StatusEnum { UP, DOWN }
        public string Name { get; set; }
        public int Online { get; set; }
        public DateTime OnlineLastModified { get; set; }
        public TimeSpan Uptime { get; set; }
        public DateTime UptimeLastModified { get; set; }

        public StatusEnum Status { get; set; }
    }
}
