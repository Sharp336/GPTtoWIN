using System.Diagnostics;
using System.Windows;
using wtp.Extensions;

namespace wtp.Terminal
{
    internal class TerminalSessionManager
    {
        
        public TerminalSession CreateSession(TerminalSize size, Profile profile)
        {
            PrepareTTermEnvironment(profile);

            var session = new TerminalSession(size, profile);
            return session;
        }

        private void PrepareTTermEnvironment(Profile profile)
        {
            var env = profile.EnvironmentVariables;

            // Force our own environment variable so shells know we are in a tterm session
            int pid = Process.GetCurrentProcess().Id;
            env[EnvironmentVariables.TTERM] = pid.ToString();

            // Add assembly directory to PATH so tterm can be launched from the shell
            var app = Application.Current as App;
            string path = env.GetValueOrDefault(EnvironmentVariables.PATH);
            if (!string.IsNullOrEmpty(path))
            {
                path = ";" + path;
            }
            env[EnvironmentVariables.PATH] = app.AssemblyDirectory + path;
        }
    }
}
