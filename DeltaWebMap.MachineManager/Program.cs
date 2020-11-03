using DeltaWebMap.MachineManager.Framework;
using LibDeltaSystem;
using LibDeltaSystem.Entities.RouterServer;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;

namespace DeltaWebMap.MachineManager
{
    class Program
    {
        public const byte APP_VERSION_MAJOR = 0;
        public const byte APP_VERSION_MINOR = 1;

        public static DateTime startTime;
        public static RouterServerConfig connectionConfig;
        public static ManagerSession session;

        static void Main(string[] args)
        {
            //Setup
            startTime = DateTime.UtcNow;

            //Open config files
            connectionConfig = JsonConvert.DeserializeObject<RouterServerConfig>(File.ReadAllText(args[0]));

            //Make session
            session = ManagerSession.LoadSession(args[1]);

            //Run
            session.Run().GetAwaiter().GetResult();
        }
    }
}
