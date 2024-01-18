using System;

namespace tterm
{
    public class RemoteControl : MarshalByRefObject
    {

        public void IsInstalled(int InPID)
        {

        }

        public void ReadCalled()
        {
            Console.WriteLine("ReadCalled");
        }

        public void ReceivedMessage(string msgPacket)
        {

        }

        public void HandleError(Exception e)
        {
            Console.WriteLine(e.ToString());
        }
    }
}
