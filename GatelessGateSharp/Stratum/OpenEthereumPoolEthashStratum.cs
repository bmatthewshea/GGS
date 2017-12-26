using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Net;
using System.Net.Sockets;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using HashLib;


namespace GatelessGateSharp
{
    class OpenEthereumPoolEthashStratum : EthashStratum
    {
        public new class Work
        {
            readonly private Job mJob;
            readonly private byte mLocalExtranonce;

            public Job CurrentJob { get { return mJob; } }
            public byte LocalExtranonce { get { return mLocalExtranonce; } }

            public Work(Job aJob)
            {
                mJob = aJob;
                mLocalExtranonce = mJob.GetNewLocalExtranonce();
            }
        }

        public new class Job : EthashStratum.Job
        {
            private byte mExtranonce = 0;

            public byte Extranonce { get { return mExtranonce; } }

            public Job(string aID, string aSeedhash, string aHeaderhash) 
                : base(aID, aSeedhash, aHeaderhash)
            {
            }
        }

        Thread mPingThread;
        int mJsonRPCMessageID = 1;
        private Mutex mMutex = new Mutex();

        protected override void ProcessLine(String line)
        {
            Dictionary<String, Object> response = JsonConvert.DeserializeObject<Dictionary<string, Object>>(line);
            if (response.ContainsKey("result")
                && response["result"] == null
                && response.ContainsKey("error") && response["error"].GetType() == typeof(String))
            {
                MainForm.Logger("Stratum server responded: " + (String)response["error"]);
            }
            else if (response.ContainsKey("result")
                && response["result"] == null
                && response.ContainsKey("error") && response["error"].GetType() == typeof(Newtonsoft.Json.Linq.JObject))
            {
                MainForm.Logger("Stratum server responded: " + ((JContainer)response["error"])["message"]);
            }
            else if (response.ContainsKey("result")
                    && response["result"] == null)
            {
                MainForm.Logger("Share #" + response["id"].ToString() + " rejected.");
                ReportShareRejection();
            }
            else if (response.ContainsKey("result")
                && response["result"] != null
                && response["result"].GetType() == typeof(bool))
            {
                if ((bool)response["result"] && !MainForm.DevFeeMode)
                {
                    MainForm.Logger("Share #" + response["id"].ToString() + " accepted.");
                    ReportShareAcceptance();
                }
                else if (response.ContainsKey("error") && response["error"].GetType() == typeof(String) && !MainForm.DevFeeMode)
                {
                    MainForm.Logger("Share #" + response["id"].ToString() + " rejected: " + (String)response["error"]);
                    ReportShareRejection();
                }
                else if (response.ContainsKey("error") && response["error"].GetType() == typeof(JArray) && !MainForm.DevFeeMode)
                {
                    MainForm.Logger("Share #" + response["id"].ToString() + " rejected: " + ((JArray)response["error"])["message"]);
                    ReportShareRejection();
                }
                else if (!(bool)response["result"] && !MainForm.DevFeeMode)
                {
                    MainForm.Logger("Share #" + response["id"].ToString() + " rejected.");
                    ReportShareRejection();
                }
                else 
                {
                    MainForm.Logger("Unknown JSON message: " + line);
                }
            }
            else if (response.ContainsKey("result")
                        && response["result"] != null
                        && response["result"].GetType() == typeof(JArray))
            {
                var ID = response["id"];
                JArray result = (JArray)response["result"];
                var oldJob = mJob;
                if (oldJob == null || ("0x" + oldJob.ID) != (string)result[0])
                {
                    mMutex.WaitOne();
                    System.Text.RegularExpressions.Regex regex = new System.Text.RegularExpressions.Regex(@"^0x");
                    mJob = (EthashStratum.Job)(new Job(
                        regex.Replace((string)result[0], ""), // Use headerhash as job ID.
                        regex.Replace((string)result[1], ""),
                        regex.Replace((string)result[0], "")));
                    regex = new System.Text.RegularExpressions.Regex(@"^0x(.*)................................................$"); // I don't know about this one...
                    mDifficulty = (double)0xffff0000U / (double)Convert.ToUInt64(regex.Replace((string)result[2], "$1"), 16);
                    mMutex.ReleaseMutex();
                    MainForm.Logger("Received new job: " + (string)result[0]);
                }
            }
            else
            {
                MainForm.Logger("Unknown JSON message: " + line);
            }
        }
        
        // This is for DwarfPool.
        private void PingThread()
        {
            System.Threading.Thread.Sleep(5000);

            while (!Stopped)
            {
                try
                {
                    mMutex.WaitOne();

                    WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new Dictionary<string, Object> {
                        { "id", mJsonRPCMessageID++ },
                        { "jsonrpc", "2.0" },
                        { "method", "eth_getWork" }
                    }));

                    mMutex.ReleaseMutex();
                }
                catch (Exception ex)
                {
                    MainForm.Logger("Exception in ping thread: " + ex.Message + ex.StackTrace);
                }

                System.Threading.Thread.Sleep(5000);
            }
        }

        override protected void Authorize()
        {
            mMutex.WaitOne();

            WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new Dictionary<string, Object> {
                { "id", mJsonRPCMessageID++ },
                { "jsonrpc", "2.0" },
                { "method", "eth_submitLogin" },
                { "params", new List<string> {
                    Username
            }}}));

            var response = JsonConvert.DeserializeObject<Dictionary<string, Object>>(ReadLine());
            if (response["result"] == null)
            {
                mMutex.ReleaseMutex();
                MainForm.Logger("Authorization failed.");
                throw new Exception("Authorization failed.");
            }

            WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new Dictionary<string, Object> {
                { "id", mJsonRPCMessageID++ },
                { "jsonrpc", "2.0" },
                { "method", "eth_getWork" }
            }));

            mMutex.ReleaseMutex();

            mPingThread = new Thread(new ThreadStart(PingThread));
            mPingThread.IsBackground = true;
            mPingThread.Start();
        }

        override public void Submit(Device aDevice, EthashStratum.Job job, UInt64 output)
        {
            if (Stopped)
                return;

            mMutex.WaitOne();
            RegisterDeviceWithShare(aDevice);
            try
            {
                String stringNonce
                      = String.Format("{7:x2}{6:x2}{5:x2}{4:x2}{3:x2}{2:x2}{1:x2}{0:x2}",
                                      ((output >> 0) & 0xff),
                                      ((output >> 8) & 0xff),
                                      ((output >> 16) & 0xff),
                                      ((output >> 24) & 0xff),
                                      ((output >> 32) & 0xff),
                                      ((output >> 40) & 0xff),
                                      ((output >> 48) & 0xff),
                                      ((output >> 56) & 0xff));
                String message = JsonConvert.SerializeObject(new Dictionary<string, Object> {
                    { "id", mJsonRPCMessageID++ },
                    { "jsonrpc", "2.0" },
                    { "method", "eth_submitWork" },
                    { "params", new List<string> {
                        "0x" + stringNonce,
                        "0x" + job.Headerhash, // The header's pow-hash (256 bits)
                        "0x" + job.GetMixHash(output) // mix digest
                }}});
                WriteLine(message);
                MainForm.Logger("Device #" + aDevice.DeviceIndex + " submitted a share.");
            }
            catch (Exception ex)
            {
                MainForm.Logger("Failed to submit share: " + ex.Message + ex.StackTrace);
            }
            mMutex.ReleaseMutex();
        }

        public OpenEthereumPoolEthashStratum(String aServerAddress, int aServerPort, String aUsername, String aPassword, String aPoolName)
            : base(aServerAddress, aServerPort, aUsername, aPassword, aPoolName)
        {
        }
    }
}
