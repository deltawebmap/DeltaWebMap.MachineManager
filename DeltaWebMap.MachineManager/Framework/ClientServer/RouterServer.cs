using DeltaWebMap.MachineManager.Framework.Entities;
using LibDeltaSystem;
using LibDeltaSystem.CoreNet;
using LibDeltaSystem.CoreNet.IO;
using LibDeltaSystem.CoreNet.IO.Server;
using LibDeltaSystem.CoreNet.IO.Transports;
using LibDeltaSystem.CoreNet.NetMessages;
using LibDeltaSystem.Entities;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace DeltaWebMap.MachineManager.Framework.ClientServer
{
    public class RouterServer
    {
        private ServerRouterIO<RouterSession> io;
        private IPEndPoint listenEndpoint;
        private IDeltaLogger logger;
        private ManagerSession session;
        
        public RouterServer(ManagerSession session)
        {
            //Open IO
            this.session = session;
            this.logger = session;
            listenEndpoint = new IPEndPoint(IPAddress.Loopback, session.private_port);
            io = new ServerRouterIO<RouterSession>(logger, new UnencryptedTransport(), new MinorMajorVersionPair(Program.APP_VERSION_MAJOR, Program.APP_VERSION_MINOR), listenEndpoint, (IServerRouterIO server, Socket sock) =>
            {
                return new RouterSession(server, sock);
            });
            io.OnClientConnected += Io_OnClientConnected;
            io.OnClientDropped += Io_OnClientDropped;
            io.OnClientMessage += Io_OnClientMessage;
        }

        private void Io_OnClientMessage(RouterSession session, RouterMessage msg)
        {
            if(session.authenticated)
            {
                //Authenticated commands
                if (msg.opcode == RouterConnection.OPCODE_SYS_GETCFG)
                    HandleRequestConfigCommand(session, msg);
                else if (msg.opcode == RouterConnection.OPCODE_SYS_USERCFG)
                    HandleRequestUserConfigCommand(session, msg);
                else if (msg.opcode == RouterConnection.OPCODE_SYS_RPC)
                    HandleRequestRPCCommand(session, msg);
                else if (msg.opcode == RouterConnection.OPCODE_SYS_LOG)
                    HandleRequestLogCommand(session, msg);
                else
                    logger.Log("Io_OnClientMessage", $"Client {session.GetDebugName()} sent an unknown command ({msg.opcode}).", DeltaLogLevel.Debug);
            } else
            {
                //Unauthenticated commands
                if (msg.opcode == RouterConnection.OPCODE_SYS_LOGIN)
                    HandleLoginCommand(session, msg);
                else
                    logger.Log("Io_OnClientMessage", $"Client {session.GetDebugName()} sent unauthenticated command that was not LOGIN ({msg.opcode}).", DeltaLogLevel.Debug);
            }
        }

        private void Io_OnClientDropped(RouterSession session)
        {
            logger.Log("Io_OnClientConnected", $"Dropped client {session.GetDebugName()}", DeltaLogLevel.Low);
        }

        private void Io_OnClientConnected(RouterSession session)
        {
            logger.Log("Io_OnClientConnected", $"Connected client {session.GetDebugName()}", DeltaLogLevel.Low);
        }

        private void HandleLoginCommand(RouterSession session, RouterMessage msg)
        {
            //Get details
            DeltaCoreNetServerType type = (DeltaCoreNetServerType)BitConverter.ToInt32(msg.payload, 0);
            long loginKey = BitConverter.ToInt64(msg.payload, 4);

            //Find the linked instance
            ManagerInstance instance = null;
            lock (this.session.instances)
            {
                foreach(var s in this.session.instances)
                {
                    if (loginKey == s.id)
                        instance = s;
                }
            }

            //If this failed, abort
            if(instance == null)
            {
                logger.Log("HandleLoginCommand", $"Logging in client {session.GetDebugName()} with key {loginKey} FAILED. Dropping client...", DeltaLogLevel.Medium);
                io.DropClient(session);
                return;
            }

            //Set properties on session
            session.authenticatedType = type;
            instance.linkedSession = session;
            session.linkedInstance = instance;
            session.authenticated = true;

            //Log
            logger.Log("HandleLoginCommand", $"Logged in client {session.GetDebugName()} as {type.ToString().ToUpper()} as {instance.id} (v {instance.version_id}).", DeltaLogLevel.Low);
        }

        private void HandleRequestConfigCommand(RouterSession session, RouterMessage msg)
        {
            //Create LoginServerInfo to return
            LoginServerInfo response = new LoginServerInfo
            {
                success = true,
                instance_id = session.linkedInstance.id.ToString(),
                user_ports = session.linkedInstance.ports,
                config = this.session.remoteConfig
            };

            //Respond
            msg.RespondJson(response, true);
        }

        private void HandleRequestUserConfigCommand(RouterSession session, RouterMessage msg)
        {
            ProxyRequestToMaster(session, msg, MasterConnectionOpcodes.OPCODE_MASTER_GETUSERCFG);
        }

        private void HandleRequestRPCCommand(RouterSession session, RouterMessage msg)
        {
            //We're going to attempt to dispatch this to all connected RPC servers locally.
            //In the future, we'll likely send this to the master and let it dispatch it to all machines and regions, but not yet
            //This will make only RPC sessions connected to this machine work, even if they're in a different process

            //Find all connected RPC clients
            var clients = this.session.GetConnectedInstancesByType(DeltaCoreNetServerType.API_RPC);

            //Dispatch
            foreach (var c in clients)
                c.linkedSession.SendMessage(RouterConnection.OPCODE_SYS_RPC, msg.payload);
        }

        private void HandleRequestLogCommand(RouterSession session, RouterMessage msg)
        {
            //Add the instance ID to the beginning of the payload, then send to the master
            byte[] data = new byte[msg.payload.Length + 8];

            //Set instance ID and copy
            BitConverter.GetBytes(session.linkedInstance.id).CopyTo(data, 0);
            msg.payload.CopyTo(data, 8);

            //Send to master
            this.session.masterConnection.SendMessage(MasterConnectionOpcodes.OPCODE_MASTER_INSTANCE_LOG, data);
        }

        private void ProxyRequestToMaster(RouterSession session, RouterMessage msg, short opcode)
        {
            var channel = this.session.masterConnection.SendMessageGetResponseChannel(opcode, msg.payload);
            ProcessProxiedMessage(channel, session, msg);
        }

        private void ProcessProxiedMessage(System.Threading.Channels.ChannelReader<RouterMessage> channel, RouterSession session, RouterMessage msg)
        {
            channel.ReadAsync().AsTask().ContinueWith((Task<RouterMessage> m) =>
            {
                msg.Respond(m.Result.payload, m.Result.flagIsLast);
                if(!m.Result.flagIsLast)
                    ProcessProxiedMessage(channel, session, msg);
            });
        }
    }
}
