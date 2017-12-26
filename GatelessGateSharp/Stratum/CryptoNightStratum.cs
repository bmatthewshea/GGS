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
    class CryptoNightStratum : Stratum
    {
        public new class Work : Stratum.Work
        {
            readonly private Job mJob;

            public new Job GetJob() { return mJob; }

            public Work(Job aJob)
                : base(aJob)
            {
                mJob = aJob;
            }
        }

        public new class Job : Stratum.Job
        {
            readonly String mID;
            readonly String mBlob;
            readonly String mTarget;

            public String ID { get { return mID; } }
            public String Blob { get { return mBlob; } }
            public String Target { get { return mTarget; } }

            public Job(string aID, string aBlob, string aTarget)
            {
                mID = aID;
                mBlob = aBlob;
                mTarget = aTarget;
            }

            public bool Equals(Job right)
            {
                return mID == right.mID && mBlob == right.mBlob && mTarget == right.mTarget;
            }
        }

        String mUserID;
        Job mJob;
        private Mutex mMutex = new Mutex();

        public Job GetJob()
        {
            return mJob;
        }

        protected override void ProcessLine(String line)
        {
            Dictionary<String, Object> response = JsonConvert.DeserializeObject<Dictionary<string, Object>>(line);
            if (response.ContainsKey("method") && response.ContainsKey("params"))
            {
                string method = (string)response["method"];
                JContainer parameters = (JContainer)response["params"];
                if (method.Equals("job"))
                {
                    try { mMutex.WaitOne(); } catch (Exception) { }
                    mJob = new Job((string)parameters["job_id"], (string)parameters["blob"], (string)parameters["target"]);
                    try { mMutex.ReleaseMutex(); } catch (Exception) { }
                    MainForm.Logger("Received new job: " + parameters["job_id"]);
                }
                else
                {
                    MainForm.Logger("Unknown stratum method: " + line);
                }
            }
            else if (response.ContainsKey("id") && response.ContainsKey("error"))
            {
                var ID = response["id"];
                var error = response["error"];

                if (error == null && !MainForm.DevFeeMode) {
                    MainForm.Logger("Share accepted.");
                    ReportShareAcceptance();
                }
                else if (error != null && !MainForm.DevFeeMode)
                {
                    MainForm.Logger("Share rejected: " + (String)(((JContainer)response["error"])["message"]));
                    ReportShareRejection();
                }
            }
            else
            {
                MainForm.Logger("Unknown JSON message: " + line);
            }
        }

        override protected void Authorize()
        {
            var line = Newtonsoft.Json.JsonConvert.SerializeObject(new Dictionary<string, Object> {
                { "method", "login" },
                { "params", new Dictionary<string, string> {
                    { "login", Username },
                    { "pass", "x" },
                    { "agent", MainForm.shortAppName + "/" + MainForm.appVersion}}},
                { "id", 1 }
            });
            WriteLine(line);

            if ((line = ReadLine()) == null)
                throw new Exception("Disconnected from stratum server.");
            Dictionary<String, Object> response = JsonConvert.DeserializeObject<Dictionary<string, Object>>(line);
            var result = ((JContainer)response["result"]);
            var status = (String)(result["status"]);
            if (status != "OK")
                throw new Exception("Authorization failed.");

            try { mMutex.WaitOne(); } catch (Exception) { }
            mUserID = (String)(result["id"]);
            mJob = new Job((String)(((JContainer)result["job"])["job_id"]), (String)(((JContainer)result["job"])["blob"]), (String)(((JContainer)result["job"])["target"]));
            try { mMutex.ReleaseMutex(); } catch (Exception) { }
        }

        public void Submit(Device device, Job job, UInt32 output, String result)
        {
            if (Stopped)
                return;

            try { mMutex.WaitOne(); } catch (Exception) { }
            RegisterDeviceWithShare(device);
            try
            {
                String stringNonce = String.Format("{0:x2}{1:x2}{2:x2}{3:x2}", ((output >> 0) & 0xff), ((output >> 8) & 0xff), ((output >> 16) & 0xff), ((output >> 24) & 0xff));
                String message = JsonConvert.SerializeObject(new Dictionary<string, Object> {
                    { "method", "submit" },
                    { "params", new Dictionary<String, String> {
                        { "id", mUserID },
                        { "job_id", job.ID },
                        { "nonce", stringNonce },
                        { "result", result }}},
                    { "id", 4 }});
                WriteLine(message);
                MainForm.Logger("Device #" + device.DeviceIndex + " submitted a share.");
            }
            catch (Exception ex)
            {
                MainForm.Logger("Failed to submit share: " + ex.Message);
            }
            try { mMutex.ReleaseMutex(); } catch (Exception) { }
        }

        public new Work GetWork()
        {
            return new Work(mJob);
        }

        public CryptoNightStratum(String aServerAddress, int aServerPort, String aUsername, String aPassword, String aPoolName)
            : base(aServerAddress, aServerPort, aUsername, aPassword, aPoolName)
        {
        }
    }
}
