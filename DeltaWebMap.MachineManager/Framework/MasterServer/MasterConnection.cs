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
    }
}
