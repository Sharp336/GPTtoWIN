using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace tterm.Remote
{
    public class RemoteControl : MarshalByRefObject
    {

        public void IsInstalled(int InPID)
        {
            Console.WriteLine("IsInstalled()");
        }

        public void ReadCalled()
        {
            Console.WriteLine("ReadCalled()");
        }

        public void ReceivedMessage(string msgPacket)
        {
            Console.WriteLine("ReceivedMessage " + msgPacket);
        }

        public void HandleError(Exception e)
        {
            Console.WriteLine(e.ToString());

        }
    }
}
