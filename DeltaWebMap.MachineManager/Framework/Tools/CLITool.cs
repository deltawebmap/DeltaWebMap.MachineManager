using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace DeltaWebMap.MachineManager.Framework.Tools
{
    public static class CLITool
    {
        public static int RunCLIProcess(string path, string args, IManagerCommandLogger logger, string logTopic, string logMessage)
        {
            //Start logging session
            logger?.LogCLIBegin(logTopic, logMessage);

            //Start process
            Process p = Process.Start(new ProcessStartInfo
            {
                FileName = path,
                Arguments = args,
                RedirectStandardOutput = true
            });

            return BaseRunProcess(p, logger);
        }

        public static int RunTerminalCommand(string cmd, IManagerCommandLogger logger, string logTopic, string logMessage)
        {
            //Start logging session
            logger?.LogCLIBegin(logTopic, logMessage);

            //Start process
            Process p;
            if(Environment.OSVersion.Platform == PlatformID.Unix)
            {
                //Linux
                p = Process.Start(new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = cmd,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
            } else
            {
                //Windows
                p = Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/C " + cmd,
                    RedirectStandardOutput = true
                });
            }

            return BaseRunProcess(p, logger);
        }

        private static int BaseRunProcess(Process p, IManagerCommandLogger logger)
        {
            //Compute
            while (!p.StandardOutput.EndOfStream)
            {
                //Get line
                string line = p.StandardOutput.ReadLine();
                logger?.LogCLIOutput(line);
            }

            //Wait for end
            if (!p.HasExited)
            {
                logger?.LogCLIOutput("Output stream closed. Waiting for process to exit...");
                p.WaitForExit();
            }

            //Log
            logger?.LogCLICompletion(p.ExitCode);

            return p.ExitCode;
        }
    }
}
