using LibDeltaSystem.CoreNet.IO;
using LibDeltaSystem.CoreNet.NetMessages.Master;
using System;
using System.Collections.Generic;
using System.Text;

namespace DeltaWebMap.MachineManager.Framework.MasterServer
{
    public class MasterCommandLogger : IManagerCommandLogger
    {
        private RouterMessage message;

        public MasterCommandLogger(RouterMessage message)
        {
            this.message = message;
        }
        
        private void _SendLog(MasterCommandLogOpcode op, int code, params string[] strings)
        {
            //Determine size
            int size = 8;
            foreach (var s in strings)
                size += 4 + Encoding.UTF8.GetByteCount(s);

            //Allocate buffer and begin writing
            byte[] b = new byte[size];
            BitConverter.GetBytes((short)op).CopyTo(b, 0);
            BitConverter.GetBytes((short)strings.Length).CopyTo(b, 2);
            BitConverter.GetBytes(code).CopyTo(b, 4);

            //Write strings
            int pos = 8;
            foreach(var s in strings)
            {
                byte[] stringBytes = Encoding.UTF8.GetBytes(s);
                BitConverter.GetBytes(stringBytes.Length).CopyTo(b, pos);
                pos += 4;
                stringBytes.CopyTo(b, pos);
                pos += stringBytes.Length;
            }

            //Send
            message.Respond(b, op == MasterCommandLogOpcode.FINISHED_FAIL || op == MasterCommandLogOpcode.FINISHED_SUCCESS);
        }
        
        public void FinishFail(string message)
        {
            _SendLog(MasterCommandLogOpcode.FINISHED_FAIL, 0, message);
        }

        public void FinishSuccess(string message)
        {
            _SendLog(MasterCommandLogOpcode.FINISHED_SUCCESS, 0, message);
        }

        public void Log(string topic, string text)
        {
            _SendLog(MasterCommandLogOpcode.LOG, 0, topic, text);
        }

        public void LogCLIBegin(string topic, string text)
        {
            _SendLog(MasterCommandLogOpcode.LOG_CLI_BEGIN, 0, topic, text);
        }

        public void LogCLICompletion(int exitCode)
        {
            _SendLog(MasterCommandLogOpcode.LOG_CLI_END, exitCode);
        }

        public void LogCLIOutput(string output)
        {
            _SendLog(MasterCommandLogOpcode.LOG_CLI_MESSAGE, 0, output);
        }
    }
}
