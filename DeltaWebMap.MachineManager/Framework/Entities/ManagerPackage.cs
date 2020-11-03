using DeltaWebMap.MachineManager.Framework.Tools;
using LibDeltaSystem.CoreNet.NetMessages.Master.Entities;
using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DeltaWebMap.MachineManager.Framework.Entities
{
    /// <summary>
    /// Represents a package of source code
    /// </summary>
    public class ManagerPackage : NetManagerPackage
    {
        public string GetGitPath(ManagerSession session)
        {
            return session.build_path + name + "/";
        }

        public string GetProjectPath(ManagerSession session)
        {
            return GetGitPath(session) + project_path;
        }

        public int DownloadUpdate(ManagerSession session, IManagerCommandLogger logger, string topic)
        {
            //Pull new code from GIT
            GitTool git = new GitTool(GetGitPath(session), logger);
            int status = git.Pull(topic, $"[{name}] Pulling updated source from Git...");

            //Loop through dependencies and update them
            foreach (var d in dependencies)
                status += session.packages[d].DownloadUpdate(session, logger, topic);

            return status;
        }

        public ManagerVersion BuildUpdatedVersion(ManagerSession session, IManagerCommandLogger logger, string topic)
        {
            //Download the update
            logger.Log(topic, "Downloading new updates...");
            if(DownloadUpdate(session, logger, topic) != 0)
            {
                logger.FinishFail("Failed to download updates. Aborting!");
                return null;
            }

            //Create new version ID
            string id = ObjectId.GenerateNewId().ToString();
            while(session.versions.ContainsKey(id))
                id = ObjectId.GenerateNewId().ToString();

            //Create new version
            ManagerVersion version = new ManagerVersion
            {
                id = id,
                package_name = name,
                time = DateTime.UtcNow
            };

            //Create output folder
            Directory.CreateDirectory(version.GetPath(session));

            //Build new version
            logger.Log(topic, "Building version from source...");
            if(CLITool.RunCLIProcess(session.dotnet_path, $"build -o {version.GetPath(session)} {GetProjectPath(session)}", logger, topic, "Building project files...") != 0)
            {
                logger.FinishFail("Failed to build project. Aborting!");
                return null;
            }

            //Add to versions
            lock (session.versions)
                session.versions.Add(id, version);
            latest_version = version.id;
            session.Save();

            //Log
            logger.FinishSuccess($"Successfully created new version {version.id} for package {version.package_name}.");

            return version;
        }
    }
}
