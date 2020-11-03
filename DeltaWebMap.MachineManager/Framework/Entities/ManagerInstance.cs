using DeltaWebMap.MachineManager.Framework.ClientServer;
using LibDeltaSystem;
using LibDeltaSystem.CoreNet.NetMessages.Master.Entities;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace DeltaWebMap.MachineManager.Framework.Entities
{
    /// <summary>
    /// Represents an actual instance of a version
    /// </summary>
    public class ManagerInstance : NetManagerInstance
    {
        [JsonIgnore]
        private Process instance;

        [JsonIgnore]
        public RouterSession linkedSession;

        public ManagerPackage GetPackage(ManagerSession session)
        {
            return session.packages[package_name];
        }

        public ManagerVersion GetVersion(ManagerSession session)
        {
            return session.versions[version_id];
        }

        public void StartInstance(ManagerSession session)
        {
            //Build args
            string args = $"{GetVersion(session).GetExecPath(session)} {session.private_port} {id}";

            //Start process
            instance = Process.Start(new ProcessStartInfo
            {
                FileName = session.dotnet_path,
                Arguments = args
            });
        }

        public void StopInstance()
        {
            if(instance != null)
            {
                instance.CloseMainWindow();
                instance.WaitForExit();
            }
            instance = null;
        }

        public void UpdateInstance(ManagerSession session, IManagerCommandLogger logger)
        {
            //Get the ID of the new version ID
            ManagerPackage package = GetPackage(session);
            string newId = package.latest_version;
            if(newId == null)
            {
                logger.FinishFail("There is no current version to upgrade to!");
                return;
            }
            if(version_id == package.latest_version)
            {
                logger.FinishSuccess("The version is already up to date.");
                return;
            }

            //Stop the current instance
            logger.Log("UpdateInstance", "Shutting down current instance...");
            StopInstance();

            //Update
            logger.Log("UpdateInstance", "Applying changes and restarting instance...");
            version_id = newId;
            StartInstance(session);

            //Save
            session.Save();

            //Done
            logger.FinishSuccess("Successfully updated instance to version " + newId + "!");
        }

        public void DestoryInstance(ManagerSession session, IManagerCommandLogger logger)
        {
            //Stop the current instance
            logger.Log("UpdateInstance", "Shutting down current instance...");
            StopInstance();

            //Remove this from the list and remove used ports
            logger.Log("UpdateInstance", "Applying changes...");
            session.instances.Remove(this);
            foreach (int port in ports)
                session.used_user_ports.Remove(port);

            //Save
            session.Save();

            //Done
            logger.FinishSuccess("Successfully removed instance.");
        }
    }
}
