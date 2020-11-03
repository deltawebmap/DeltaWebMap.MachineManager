using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace DeltaWebMap.MachineManager.Framework.Tools
{
    public class GitTool
    {
        public string path;
        public IManagerCommandLogger logger;

        public GitTool(string path, IManagerCommandLogger logger)
        {
            this.path = path;
            this.logger = logger;
        }

        private int RunCommand(string args, string logTopic, string logMessage)
        {
            return CLITool.RunCLIProcess("git", $"-C {path} {args}", logger, logTopic, logMessage);
        }

        public int Pull(string logTopic, string logMessage)
        {
            return RunCommand("pull", logTopic, logMessage);
        }

        public int Clone(string gitUrl, string logTopic, string logMessage)
        {
            return CLITool.RunCLIProcess("git", $"clone {gitUrl} {path}", logger, logTopic, logMessage);
        }
    }
}
