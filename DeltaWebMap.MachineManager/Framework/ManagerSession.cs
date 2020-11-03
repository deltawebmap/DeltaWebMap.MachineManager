﻿using DeltaWebMap.MachineManager.Framework.ClientServer;
using DeltaWebMap.MachineManager.Framework.Entities;
using DeltaWebMap.MachineManager.Framework.MasterServer;
using DeltaWebMap.MachineManager.Framework.Tools;
using LibDeltaSystem;
using LibDeltaSystem.CoreNet.NetMessages;
using LibDeltaSystem.CoreNet.NetMessages.Master;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace DeltaWebMap.MachineManager.Framework
{
    public class ManagerSession : IDeltaLogger
    {
        public Dictionary<string, ManagerPackage> packages = new Dictionary<string, ManagerPackage>();
        public Dictionary<string, ManagerVersion> versions = new Dictionary<string, ManagerVersion>();
        public List<ManagerInstance> instances = new List<ManagerInstance>();
        public List<int> used_user_ports = new List<int>();
        public string dotnet_path = "dotnet";
        public string build_path;
        public string bin_path;
        public int private_port;
        public int user_port_begin;

        [JsonIgnore]
        public MasterConnection masterConnection;
        [JsonIgnore]
        public RouterServer routerServer;
        [JsonIgnore]
        public string cfgPath;
        [JsonIgnore]
        public Random rand;
        [JsonIgnore]
        public LoginServerConfig remoteConfig;

        public static ManagerSession LoadSession(string cfgPath)
        {
            //Load cfg
            ManagerSession session = JsonConvert.DeserializeObject<ManagerSession>(File.ReadAllText(cfgPath));

            //Misc
            session.rand = new Random();
            session.cfgPath = cfgPath;

            //Open connection to master
            session.masterConnection = new MasterConnection(session);
            session.Log("LoadSession", "Waiting for login with master to complete...", DeltaLogLevel.Medium);
            while (!session.masterConnection.loggedIn) ;

            //Request config
            session.Log("LoadSession", "Requested remote config from master control. Waiting...", DeltaLogLevel.Medium);
            session.remoteConfig = session.masterConnection.RequestConfigFile().GetAwaiter().GetResult();
            session.Log("LoadSession", "Got remote config successfully.", DeltaLogLevel.Medium);

            //Open server
            session.routerServer = new RouterServer(session);

            return session;
        }

        public async Task Run()
        {
            await Task.Delay(-1);
        }

        public void Save()
        {
            File.WriteAllText(cfgPath, JsonConvert.SerializeObject(this, Formatting.Indented));
        }

        public void Log(string topic, string message, DeltaLogLevel level)
        {
            Console.WriteLine($"[{level.ToString().ToUpper().PadLeft(6, ' ')}] [{topic}] {message}");
        }

        public int ClaimNewUserPort()
        {
            int port = user_port_begin;
            while (used_user_ports.Contains(port))
                port++;
            used_user_ports.Add(port);
            return port;
        }

        public ManagerPackage AddPackage(ManagerAddPackage cmd, IManagerCommandLogger logger)
        {
            //Make sure the package doesn't already exist
            if (packages.ContainsKey(cmd.name))
            {
                logger.FinishFail("The package requested already exists.");
                return null;
            }

            //Make sure all requested dependencies exist
            foreach (var d in cmd.dependencies)
            {
                if (!packages.ContainsKey(d))
                {
                    logger.FinishFail($"The requested dependency \"{d}\" does not exist.");
                    return null;
                }
            }

            //Create package
            ManagerPackage package = new ManagerPackage
            {
                name = cmd.name,
                project_path = cmd.project_path,
                git_repo = cmd.git_repo,
                exec = cmd.exec,
                required_user_ports = cmd.required_user_ports,
                dependencies = cmd.dependencies
            };

            //Clone GIT directory
            logger.Log("AddPackage", "Cloning source project files...");
            int status = new GitTool(package.GetGitPath(this), logger).Clone(cmd.git_repo, "AddPackage", "Cloning...");
            if(status != 0)
            {
                logger.FinishFail("Failed to clone source project files. Aborting!");
                return null;
            }

            //Add
            packages.Add(cmd.name, package);
            Save();

            //Finish
            logger.FinishSuccess($"Successfully added package {package.name}.");

            return package;
        }

        public ManagerInstance[] CreateNewInstance(ManagerPackage package, IManagerCommandLogger logger, int count = 1, bool spawn = true)
        {
            //Make sure this has a version
            if (package.latest_version == null)
            {
                logger?.FinishFail("Package has no version. Please compile a new version before continuing.");
                return null;
            }

            //Loop through creation
            ManagerInstance[] newInstances = new ManagerInstance[count];
            for(int i = 0; i<count; i++)
            {
                //Generate a new ID
                long id;
                while(true)
                {
                    //Create
                    byte[] idBytes = new byte[8];
                    rand.NextBytes(idBytes);
                    id = Math.Abs(BitConverter.ToInt64(idBytes));

                    //Validate
                    bool exists = false;
                    foreach (var s in instances)
                        exists = exists || s.id == id;
                    if (!exists)
                        break;
                }

                //Allocate ports
                int[] ports = new int[package.required_user_ports];
                for (int p = 0; p < ports.Length; p++)
                    ports[p] = ClaimNewUserPort();

                //Create
                ManagerInstance instance = new ManagerInstance
                {
                    id = id,
                    package_name = package.name,
                    ports = ports,
                    version_id = package.latest_version
                };
                instances.Add(instance);
                newInstances[i] = instance;

                //Spawn
                if (spawn)
                    instance.StartInstance(this);

                //Log
                logger?.Log("CreateNewInstance", $"Created instance {id} with version {package.latest_version}.");
            }

            //Save
            Save();

            //Log
            logger?.FinishSuccess("Created " + newInstances.Length + " instances.");
            return newInstances;
        }

        public ManagerInstance GetInstanceById(long id)
        {
            foreach(var i in instances)
            {
                if (i.id == id)
                    return i;
            }
            return null;
        }
    }
}