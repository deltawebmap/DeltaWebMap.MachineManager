using DeltaWebMap.MachineManager.Framework.Entities;
using LibDeltaSystem;
using LibDeltaSystem.CoreNet;
using LibDeltaSystem.CoreNet.IO;
using LibDeltaSystem.CoreNet.IO.Client;
using LibDeltaSystem.CoreNet.IO.Transports;
using LibDeltaSystem.CoreNet.NetMessages;
using LibDeltaSystem.CoreNet.NetMessages.Master;
using LibDeltaSystem.Entities;
using LibDeltaSystem.Tools;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace DeltaWebMap.MachineManager.Framework.MasterServer
{
    public class MasterConnection : ClientRouterIO
    {
        public ManagerSession session;
        public bool loggedIn;
        
        public MasterConnection(ManagerSession session) : base(session, new UnencryptedTransport(), new MinorMajorVersionPair(Program.APP_VERSION_MAJOR, Program.APP_VERSION_MINOR), new IPEndPoint(IPAddress.Parse(Program.connectionConfig.master_ip), Program.connectionConfig.master_port))
        {
            this.session = session;
            OnConnected += MasterConnection_OnConnected;
            OnRouterReceiveMessage += MasterConnection_OnRouterReceiveMessage;
        }

        public Task<LoginServerConfig> RequestConfigFile()
        {
            return RequestGetObject<LoginServerConfig>(MasterConnectionOpcodes.OPCODE_MASTER_CONFIG);
        }

        private void MasterConnection_OnRouterReceiveMessage(RouterMessage msg)
        {
            if (msg.opcode == MasterConnectionOpcodes.OPCODE_MASTER_STATUS)
                OnCmdGetStatus(msg);
            else if (msg.opcode == MasterConnectionOpcodes.OPCODE_MASTER_M_ADDPACKAGE)
                OnCmdAddPackage(msg);
            else if (msg.opcode == MasterConnectionOpcodes.OPCODE_MASTER_M_ADDVERSION)
                OnCmdAddVersion(msg);
            else if (msg.opcode == MasterConnectionOpcodes.OPCODE_MASTER_M_ADDINSTANCE)
                OnCmdAddInstance(msg);
            else if (msg.opcode == MasterConnectionOpcodes.OPCODE_MASTER_M_LISTPACKAGES)
                msg.RespondJson(MiscTools.DictToList(session.packages), true);
            else if (msg.opcode == MasterConnectionOpcodes.OPCODE_MASTER_M_LISTVERSIONS)
                msg.RespondJson(MiscTools.DictToList(session.versions), true);
            else if (msg.opcode == MasterConnectionOpcodes.OPCODE_MASTER_M_LISTINSTANCES)
                msg.RespondJson(session.instances, true);
            else if (msg.opcode == MasterConnectionOpcodes.OPCODE_MASTER_M_UPDATEINSTANCE)
                OnCmdUpdateInstance(msg);
            else if (msg.opcode == MasterConnectionOpcodes.OPCODE_MASTER_M_DESTROYINSTANCE)
                OnCmdRemoveInstance(msg);
            else if (msg.opcode == MasterConnectionOpcodes.OPCODE_MASTER_M_DELETEVERSION)
                OnCmdDeleteVersion(msg);
            else if (msg.opcode == MasterConnectionOpcodes.OPCODE_MASTER_M_ADDSITE)
                OnCmdAddSite(msg);
            else if (msg.opcode == MasterConnectionOpcodes.OPCODE_MASTER_M_LISTSITES)
                msg.RespondJson(MiscTools.DictToList(session.sites), true);
            else if (msg.opcode == MasterConnectionOpcodes.OPCODE_MASTER_M_ASSIGNSITE)
                OnCmdAssignSite(msg);
            else if (msg.opcode == MasterConnectionOpcodes.OPCODE_MASTER_REBOOT_INSTANCE)
                OnCmdRebootInstance(msg);
            else if (msg.opcode == MasterConnectionOpcodes.OPCODE_MASTER_PING_INSTANCE)
                OnCmdGetInstanceStatus(msg).GetAwaiter().GetResult();
            else
                Log("MasterConnection_OnRouterReceiveMessage", $"Got message with unknown opcode {msg.opcode}.", DeltaLogLevel.Medium);
        }

        private void MasterConnection_OnConnected()
        {
            //Create login data
            byte[] sendBuffer = new byte[20];
            BitConverter.GetBytes(Program.connectionConfig.id).CopyTo(sendBuffer, 0);
            Array.Copy(Program.connectionConfig.auth_key, 0, sendBuffer, 2, 16);

            //Send
            SendMessage(MasterConnectionOpcodes.OPCODE_MASTER_LOGIN, sendBuffer);
            loggedIn = true;
        }

        private void OnCmdGetStatus(RouterMessage msg)
        {
            //Produce and respond with thatus
            msg.RespondJson(new ManagerStatusMessage
            {
                host_name = System.Environment.MachineName,
                host_os = System.Environment.OSVersion.Platform.ToString().ToUpper(),
                current_time = DateTime.UtcNow,
                start_time = Program.startTime,
                version_app_major = Program.APP_VERSION_MAJOR,
                version_app_minor = Program.APP_VERSION_MINOR,
                version_lib_major = DeltaConnection.LIB_VERSION_MAJOR,
                version_lib_minor = DeltaConnection.LIB_VERSION_MINOR
            }, true);
        }

        private async Task OnCmdGetInstanceStatus(RouterMessage msg)
        {
            //Create buffer for output. It consists of this format, each a byte unless otherwise stated: [status, reserved, appVersionMajor, appVersionMinor, libVersionMajor, libVersionMinor, (ushort)time]
            byte[] buffer = new byte[8];
            buffer[0] = (byte)InstanceStatusResult.NOT_CONNECTED;

            //Find instance
            ManagerInstance instance = session.GetInstanceById(BitConverter.ToInt64(msg.payload, 0));
            if (instance != null && instance.linkedSession != null)
            {
                try
                {
                    //Send ping
                    DateTime start = DateTime.UtcNow;
                    var pingResult = await instance.linkedSession.SendPing(4000);
                    if (pingResult != null)
                    {
                        //Success
                        buffer[0] = (byte)InstanceStatusResult.ONLINE;
                        buffer[2] = pingResult.Value.lib_version_major;
                        buffer[3] = pingResult.Value.lib_version_minor;
                        buffer[4] = pingResult.Value.app_version_major;
                        buffer[5] = pingResult.Value.app_version_minor;
                        BitConverter.GetBytes((ushort)Math.Min(ushort.MaxValue, (DateTime.UtcNow - start).TotalMilliseconds)).CopyTo(buffer, 6);
                    } else
                    {
                        //Timed out
                        buffer[0] = (byte)InstanceStatusResult.PING_TIMED_OUT;
                    }
                } catch
                {
                    buffer[0] = (byte)InstanceStatusResult.PING_FAILED;
                }
            }

            //Send
            msg.Respond(buffer, true);
        }

        private void OnCmdAddPackage(RouterMessage msg)
        {
            //Decode arguments and create logger
            ManagerAddPackage args = msg.DeserializeAs<ManagerAddPackage>();
            MasterCommandLogger logger = new MasterCommandLogger(msg);

            //Run
            try
            {
                session.AddPackage(args, logger);
            } catch (Exception ex)
            {
                logger.FinishFail($"Unexpected error: {ex.Message}{ex.StackTrace}");
            }
        }

        private void OnCmdAddVersion(RouterMessage msg)
        {
            //Decode arguments and create logger
            ManagerAddVersion args = msg.DeserializeAs<ManagerAddVersion>();
            MasterCommandLogger logger = new MasterCommandLogger(msg);

            //Find package
            if(!session.packages.ContainsKey(args.package_name))
            {
                logger.FinishFail("Could not find that package on the server.");
                return;
            }
            ManagerPackage package = session.packages[args.package_name];

            //Run
            try
            {
                package.BuildUpdatedVersion(session, logger, "BuildPackage");
            }
            catch (Exception ex)
            {
                logger.FinishFail($"Unexpected error: {ex.Message}{ex.StackTrace}");
            }
        }

        private void OnCmdAddInstance(RouterMessage msg)
        {
            //Decode arguments and create logger
            ManagerAddInstance args = msg.DeserializeAs<ManagerAddInstance>();
            MasterCommandLogger logger = new MasterCommandLogger(msg);

            //Find package
            if (!session.packages.ContainsKey(args.package_name))
            {
                logger.FinishFail("Could not find that package on the server.");
                return;
            }
            ManagerPackage package = session.packages[args.package_name];

            //Run
            try
            {
                session.CreateNewInstance(package, logger, args.count, true);
            }
            catch (Exception ex)
            {
                logger.FinishFail($"Unexpected error: {ex.Message}{ex.StackTrace}");
            }
        }

        private void OnCmdUpdateInstance(RouterMessage msg)
        {
            //Decode arguments and create logger
            ManagerUpdateInstance args = msg.DeserializeAs<ManagerUpdateInstance>();
            MasterCommandLogger logger = new MasterCommandLogger(msg);

            //Find instance
            ManagerInstance instance = session.GetInstanceById(long.Parse(args.instance_id));
            if (instance == null)
            {
                logger.FinishFail("Could not find that instance on the server.");
                return;
            }

            //Run
            try
            {
                instance.UpdateInstance(session, logger);
            }
            catch (Exception ex)
            {
                logger.FinishFail($"Unexpected error: {ex.Message}{ex.StackTrace}");
            }
        }

        private void OnCmdRemoveInstance(RouterMessage msg)
        {
            //Decode arguments and create logger
            ManagerUpdateInstance args = msg.DeserializeAs<ManagerUpdateInstance>();
            MasterCommandLogger logger = new MasterCommandLogger(msg);

            //Find instance
            ManagerInstance instance = session.GetInstanceById(long.Parse(args.instance_id));
            if (instance == null)
            {
                logger.FinishFail("Could not find that instance on the server.");
                return;
            }

            //Run
            try
            {
                instance.DestoryInstance(session, logger);
            }
            catch (Exception ex)
            {
                logger.FinishFail($"Unexpected error: {ex.Message}{ex.StackTrace}");
            }
        }

        private void OnCmdDeleteVersion(RouterMessage msg)
        {
            //Decode arguments and create logger
            ManagerDeleteVersion args = msg.DeserializeAs<ManagerDeleteVersion>();
            MasterCommandLogger logger = new MasterCommandLogger(msg);

            //Find package
            if (!session.versions.ContainsKey(args.version_id))
            {
                logger.FinishFail("Could not find that version on the server.");
                return;
            }
            ManagerVersion version = session.versions[args.version_id];

            //Run
            try
            {
                version.DeleteVersion(session, logger);
            }
            catch (Exception ex)
            {
                logger.FinishFail($"Unexpected error: {ex.Message}{ex.StackTrace}");
            }
        }

        private void OnCmdAddSite(RouterMessage msg)
        {
            //Decode arguments and create logger
            ManagerAddSite args = msg.DeserializeAs<ManagerAddSite>();
            MasterCommandLogger logger = new MasterCommandLogger(msg);

            //Run
            try
            {
                session.AddSite(args, logger);
            }
            catch (Exception ex)
            {
                logger.FinishFail($"Unexpected error: {ex.Message}{ex.StackTrace}");
            }
        }

        private void OnCmdAssignSite(RouterMessage msg)
        {
            //Decode arguments and create logger
            ManagerAssignSite args = msg.DeserializeAs<ManagerAssignSite>();
            MasterCommandLogger logger = new MasterCommandLogger(msg);

            //Update if it was supposed to be null
            if (args.site_id == "")
                args.site_id = null;

            //Find instance
            ManagerInstance instance = session.GetInstanceById(long.Parse(args.instance_id));
            if (instance == null)
            {
                logger.FinishFail("Could not find that instance on the server.");
                return;
            }

            //Run
            try
            {
                //Update
                instance.site_id = args.site_id;
                session.Save();
                session.RefreshSites();
            }
            catch (Exception ex)
            {
                logger.FinishFail($"Unexpected error: {ex.Message}{ex.StackTrace}");
            }
        }

        private void OnCmdRebootInstance(RouterMessage msg)
        {
            //Decode arguments and create logger
            ManagerRebootInstance args = msg.DeserializeAs<ManagerRebootInstance>();
            MasterCommandLogger logger = new MasterCommandLogger(msg);

            //Find instance
            ManagerInstance instance = session.GetInstanceById(long.Parse(args.instance_id));
            if (instance == null)
            {
                logger.FinishFail("Could not find that instance on the server.");
                return;
            }

            //Run
            try
            {
                //Shut down instance
                logger.Log("REBOOT", "Shutting down instance...");
                bool graceful = instance.StopInstance();
                if(!graceful)
                    logger.Log("REBOOT", "Instance was shut down forcefully!");

                //Start
                instance.StartInstance(session);
            }
            catch (Exception ex)
            {
                logger.FinishFail($"Unexpected error: {ex.Message}{ex.StackTrace}");
            }
        }
    }
}
