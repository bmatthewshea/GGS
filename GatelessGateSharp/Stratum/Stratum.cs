using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using System.IO;



namespace GatelessGateSharp
{
    class Stratum
    {
        public class Job
        {
            private Mutex mMutex = new Mutex();
            static Random r = new Random();
            byte nextLocalExtranonce;

            public Job()
            {
                try { mMutex.WaitOne(); } catch (Exception) { }
                nextLocalExtranonce = (byte)(r.Next(0, int.MaxValue) & (0xffu));
                try { mMutex.ReleaseMutex(); } catch (Exception) { }
            }

            public byte GetNewLocalExtranonce()
            {
                try { mMutex.WaitOne(); } catch (Exception) { }
                byte ret = nextLocalExtranonce++;
                try { mMutex.ReleaseMutex(); } catch (Exception) { }
                return ret;
            }
        }

        public class Work
        {
            readonly private Job mJob;
            readonly private byte mLocalExtranonce;

            public byte LocalExtranonce { get { return mLocalExtranonce; } }
            public Job GetJob() { return mJob; }

            protected Work(Job aJob)
            {
                mJob = aJob;
                mLocalExtranonce = aJob.GetNewLocalExtranonce();
            }
        }

        protected Work GetWork()
        {
            return null;
        }

        private Mutex mMutex = new Mutex();
        private bool mStopped = false;
        String mServerAddress;
        int mServerPort;
        String mUsername;
        String mPassword;
        protected double mDifficulty = 1.0;
        protected String mPoolExtranonce = "";
        protected String mPoolName = "";
        TcpClient mClient;
        NetworkStream mStream;
        StreamReader mStreamReader;
        StreamWriter mStreamWriter;
        Thread mStreamReaderThread;
        private List<Device> mDevicesWithShare = new List<Device>();

        public bool Stopped { get { return mStopped; } }
        public String ServerAddress { get { return mServerAddress; } }
        public int ServerPort { get { return mServerPort; } }
        public String Username { get { return mUsername; } }
        public String Password { get { return mPassword; } }
        public String PoolExtranonce { get { return mPoolExtranonce; } }
        public String PoolName { get { return mPoolName; } }
        public double Difficulty { get { return mDifficulty; } }

        protected void RegisterDeviceWithShare(Device aDevice)
        {
            try { mMutex.WaitOne(); } catch (Exception) { }
            mDevicesWithShare.Add(aDevice);
            try { mMutex.ReleaseMutex(); } catch (Exception) { }
        }

        protected void ReportShareAcceptance()
        {
            if (MainForm.DevFeeMode)
                return;
            try { mMutex.WaitOne(); } catch (Exception) { }
            if (mDevicesWithShare.Count > 0)
            {
                Device device = mDevicesWithShare[0];
                mDevicesWithShare.RemoveAt(0);
                device.IncrementAcceptedShares();
            }
            try { mMutex.ReleaseMutex(); } catch (Exception) { }
        }

        protected void ReportShareRejection()
        {
            if (MainForm.DevFeeMode)
                return;
            try { mMutex.WaitOne(); } catch (Exception) { }
            if (mDevicesWithShare.Count > 0)
            {
                Device device = mDevicesWithShare[0];
                mDevicesWithShare.RemoveAt(0);
                device.IncrementRejectedShares();
            }
            try { mMutex.ReleaseMutex(); } catch (Exception) { }
        }

        public void Stop()
        {
            mStopped = true;
        }

        public Stratum(String aServerAddress, int aServerPort, String aUsername, String aPassword, String aPoolName)
        {
            mServerAddress = aServerAddress;
            mServerPort = aServerPort;
            mUsername = aUsername;
            mPassword = aPassword;
            mPoolName = aPoolName;

            Connect();
        }

        public void Connect() 
        {
            try
            {
                if (Stopped)
                    return;

                MainForm.Logger("Connecting to " + ServerAddress + ":" + ServerPort + " as " + Username + "...");

                try { mMutex.WaitOne(); } catch (Exception) { }

                mClient = new TcpClient(ServerAddress, ServerPort);
                mStream = mClient.GetStream();
                mStreamReader = new StreamReader(mStream, System.Text.Encoding.ASCII, false);
                mStreamWriter = new StreamWriter(mStream, System.Text.Encoding.ASCII);

                try { mMutex.ReleaseMutex(); } catch (Exception) { }

                Authorize();

                mStreamReaderThread = new Thread(new ThreadStart(StreamReaderThread));
                mStreamReaderThread.IsBackground = true;
                mStreamReaderThread.Start();
            }
            catch (Exception ex)
            {
                MainForm.Logger("Exception in Stratum.Connect(): " + ex.ToString());
            }
        }

        protected void WriteLine(String line)
        {
            try { mMutex.WaitOne(); } catch (Exception) { }
            mStreamWriter.Write(line);
            mStreamWriter.Write("\n");
            mStreamWriter.Flush();
            try { mMutex.ReleaseMutex(); } catch (Exception) { }
        }

        protected String ReadLine()
        {
            return mStreamReader.ReadLine();
        }

        protected virtual void Authorize() { }
        protected virtual void ProcessLine(String line) { }

        private void StreamReaderThread()
        {
            try
            {
                while (!Stopped)
                {
                    string line;
                    if ((line = mStreamReader.ReadLine()) == null)
                        throw new Exception("Disconnected from stratum server.");
                    if (Stopped)
                        break;
                    ProcessLine(line);
                }
            }
            catch (Exception ex)
            {
                MainForm.Logger("Exception in Stratum.StreamReaderThread(): " + ex.ToString());
            }

            try
            {
                mClient.Close();
            }
            catch (Exception ex) { }

            if (!Stopped)
            {
                MainForm.Logger("Connection terminated. Reconnecting in 10 seconds...");
                for (int counter = 0; counter < 100; ++counter)
                {
                    if (Stopped)
                        break;
                    System.Threading.Thread.Sleep(100);
                }
            }

            if (!Stopped)
            {
                Thread reconnectThread = new Thread(new ThreadStart(Connect));
                reconnectThread.IsBackground = true;
                reconnectThread.Start();
            }
        }
    }
}
