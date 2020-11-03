using System;
using System.Collections.Generic;
using System.Text;

namespace DeltaWebMap.MachineManager.Framework
{
    public interface IManagerCommandLogger
    {
        public void Log(string topic, string text);
        public void LogCLIBegin(string topic, string text);
        public void LogCLIOutput(string output);
        public void LogCLICompletion(int exitCode);
        public void FinishSuccess(string message);
        public void FinishFail(string message);
    }
}
