using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace tterm.Extensions
{
    public static class ProcessExtensions
    {
        private static string FindIndexedProcessName(int pid)
        {
            var processName = Process.GetProcessById(pid).ProcessName;
            var processesByName = Process.GetProcessesByName(processName);
            string processIndexdName = null;

            for (var index = 0; index < processesByName.Length; index++)
            {
                processIndexdName = index == 0 ? processName : processName + "#" + index;
                var processId = new PerformanceCounter("Process", "ID Process", processIndexdName);
                if ((int)processId.NextValue() == pid)
                {
                    return processIndexdName;
                }
            }

            return processIndexdName;
        }

        private static Process FindPidFromIndexedProcessName(string indexedProcessName)
        {
            var parentId = new PerformanceCounter("Process", "Creating Process ID", indexedProcessName);
            return Process.GetProcessById((int)parentId.NextValue());
        }

        public static Process Parent(this Process process)
        {
            return FindPidFromIndexedProcessName(FindIndexedProcessName(process.Id));
        }

        public static int? FindCmdProcessPidWithWinptyAgentParent()
        {
            // Get all processes named "cmd.exe"
            var cmdProcesses = Process.GetProcessesByName("cmd");

            foreach (var cmdProcess in cmdProcesses)
            {
                try
                {
                    // Get parent process
                    var parentProcess = cmdProcess.Parent();

                    // Check if the parent process is "winpty-agent.exe"
                    if (parentProcess != null && parentProcess.ProcessName.Equals("winpty-agent", StringComparison.OrdinalIgnoreCase))
                    {
                        return cmdProcess.Id; // Return PID of the found cmd.exe
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.ToString());
                }
            }

            return null; // No matching process found
        }
    }
}
