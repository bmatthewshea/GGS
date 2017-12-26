using System;
using System.Threading;
using System.Diagnostics;



namespace GatelessGateSharpMonitor
{
    class Program
    {
        static void Main(string[] args)
        {
            using (var handle = new Mutex(true, "{1D2A713A-A29C-418C-BC62-2E98BD325490}"))
            {
                try { handle.WaitOne(); } catch (Exception) { }
                Thread.Sleep(5000);
                Process.Start("GatelessGateSharp.exe");
            }
        }
    }
}