using LibDeltaSystem.CoreNet.NetMessages.Master.Entities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DeltaWebMap.MachineManager.Framework.Entities
{
    /// <summary>
    /// Represents a compiled version
    /// </summary>
    public class ManagerVersion : NetManagerVersion
    {
        public ManagerPackage GetPackage(ManagerSession session)
        {
            return session.packages[package_name];
        }

        public string GetPath(ManagerSession session)
        {
            return session.bin_path + id + "/";
        }

        public string GetExecPath(ManagerSession session)
        {
            return GetPath(session) + GetPackage(session).exec;
        }

        public void DeleteVersion(ManagerSession session, IManagerCommandLogger logger)
        {
            //Make sure there aren't any instances using this version
            int uses = 0;
            foreach(var i in session.instances)
            {
                if (i.version_id == id)
                    uses++;
            }

            //Check if failed
            if(uses != 0)
            {
                logger.FinishFail($"Can't delete version. There are {uses} other instances using this version that must be updated or removed first.");
                return;
            }

            //Remove version
            session.versions.Remove(id);

            //If the current package has this set as the latest version, clear that
            var package = GetPackage(session);
            if (package != null && package.latest_version == id)
                package.latest_version = null;

            //Delete directory
            Directory.Delete(GetPath(session), true);

            //Save
            session.Save();

            //Finish
            logger.FinishSuccess("Successfully removed version.");
        }
    }
}
