using DeltaWebMap.MachineManager.Framework.Entities;
using LibDeltaSystem;
using LibDeltaSystem.CoreNet.IO.Server;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace DeltaWebMap.MachineManager.Framework.ClientServer
{
    public class RouterSession : ServerRouterSession
    {
        public bool authenticated;
        public DeltaCoreNetServerType authenticatedType;
        public ManagerInstance linkedInstance;

        public RouterSession(IServerRouterIO server, Socket sock) : base(server, sock)
        {
            authenticated = false;
            authenticatedType = (DeltaCoreNetServerType)(-1);
        }

        public override string GetDebugName()
        {
            return $"[PORT={((IPEndPoint)RemoteEndPoint).Port}, AUTH={authenticated.ToString().ToUpper()}, TYPE={authenticatedType.ToString()}, INSTANCE={linkedInstance?.id}]";
        }
    }
}
