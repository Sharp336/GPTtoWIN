using System;
using System.Runtime.InteropServices;
using System.Text;
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
                var hook = LocalHook.Create(LocalHook.GetProcAddress("Kernel32.dll", "ReadConsoleA"), new Delegates.ReadConsoleA(MyReadConsoleA), this);
                hook.ThreadACL.SetExclusiveACL(new Int32[] { 0 });
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

        [DllImport("Kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        static extern bool ReadConsoleA(IntPtr hConsoleInput, StringBuilder lpBuffer, uint nNumberOfCharsToRead, out uint lpNumberOfCharsRead, IntPtr lpReserved);
        bool MyReadConsoleA(IntPtr hConsoleInput, StringBuilder lpBuffer, uint nNumberOfCharsToRead, out uint lpNumberOfCharsRead, IntPtr lpReserved)
        {
            try
            {
                Interface.ReadCalled();
            }
            catch (Exception ex)
            {
                Interface.HandleError(ex);
            }
            return ReadConsoleA(hConsoleInput, lpBuffer, nNumberOfCharsToRead, out lpNumberOfCharsRead, lpReserved);
        }
    }

}

// Delegate for ReadConsoleA
namespace Delegates
{
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi, SetLastError = true)]
    public delegate bool ReadConsoleA(IntPtr hConsoleInput, StringBuilder lpBuffer, uint nNumberOfCharsToRead, out uint lpNumberOfCharsRead, IntPtr lpReserved);

}