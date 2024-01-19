using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using EasyHook;
using tterm.Remote;

namespace ConsoleReadObserver
{
    public class InjectionEntryPoint : EasyHook.IEntryPoint
    {

        static string ChannelName;
        RemoteControl Interface;

        // Constructor
        public InjectionEntryPoint(RemoteHooking.IContext context, string channelName)
        {
            try
            {
                Interface = RemoteHooking.IpcConnectClient<RemoteControl>(channelName);
                ChannelName = channelName;
                Interface.IsInstalled(RemoteHooking.GetCurrentProcessId());
                LogToFile("IsInstalled called");
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        // Run method
        public void Run(RemoteHooking.IContext context, string channelName)
        {
            // Set up the hook
            try
            {
                var hook = LocalHook.Create(LocalHook.GetProcAddress("KernelBase.dll", "ReadConsoleA"), new Delegates.ReadConsoleA(MyReadConsoleA), this);
                hook.ThreadACL.SetExclusiveACL(new Int32[] { 0 });
                Interface.ReadCalled();
            }
            catch (Exception e)
            {
                Interface.HandleError(e);
            }

            try
            {
                RemoteHooking.WakeUpProcess();
            }
            catch (Exception e)
            {
                Interface.HandleError(e);
            }
        }

        [DllImport("KernelBase.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        static extern bool ReadConsoleA( IntPtr hConsoleInput, StringBuilder lpBuffer, uint nNumberOfCharsToRead, out uint lpNumberOfCharsRead, IntPtr lpReserved);


        static bool MyReadConsoleA(IntPtr hConsoleInput, StringBuilder lpBuffer, uint nNumberOfCharsToRead, out uint lpNumberOfCharsRead, IntPtr lpReserved)
        {
            LogToFile("MyReadConsoleA called");
            try
            {
                ((InjectionEntryPoint)HookRuntimeInfo.Callback).Interface.ReadCalled();
            }
            catch (Exception ex)
            {
                ((InjectionEntryPoint)HookRuntimeInfo.Callback).Interface.HandleError(ex);
            }
            return ReadConsoleA(hConsoleInput, lpBuffer, nNumberOfCharsToRead, out lpNumberOfCharsRead, lpReserved);
        }

        private static void LogToFile(string message)
        {
            string filePath = @"C:\Users\Sharp\source\repos\Sharp336\GPTtoWIN\log.txt";
            using (StreamWriter writer = new StreamWriter(filePath, true))
            {
                writer.WriteLine($"{DateTime.Now}: {message}");
            }
        }
    }

}

// Delegate for ReadConsoleA
namespace Delegates
{
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi, SetLastError = true)]
    public delegate bool ReadConsoleA(IntPtr hConsoleInput, StringBuilder lpBuffer, uint nNumberOfCharsToRead, out uint lpNumberOfCharsRead, IntPtr lpReserved);

}