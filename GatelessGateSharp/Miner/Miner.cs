// Copyright 2017 Yurio Miyazawa (a.k.a zawawa) <me@yurio.net>
//
// This file is part of Gateless Gate Sharp.
//
// Gateless Gate Sharp is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// Gateless Gate Sharp is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with Gateless Gate Sharp.  If not, see <http://www.gnu.org/licenses/>.



using System;
using Cloo;


namespace GatelessGateSharp
{
    class Miner
    {
        private Device mDevice;
        private bool mStopped = false;
        private bool mDone = false;
        protected double mSpeed = 0;
        private String mAlgorithmName = "";
        private System.Threading.Thread mMinerThread = null;
        private DateTime mLastAlive = DateTime.Now;

        public Device GatelessGateDevice { get { return mDevice; } }
        public int DeviceIndex { get { return mDevice.DeviceIndex; } }
        public bool Stopped { get { return mStopped; } }
        public bool Done { get { return mDone; } }
        public double Speed { get { return mSpeed; } }
        public String AlgorithmName { get { return mAlgorithmName; } }
        public ComputeContext Context { get { return mDevice.Context; } }

        protected Miner(Device aDevice, String aAlgorithmName)
        {
            mDevice = aDevice;
            mAlgorithmName = aAlgorithmName;
        }

        ~Miner()
        {
            Stop();
            WaitForExit(5000);
            Abort();
        }

        public void Start()
        {
            mStopped = false;
            mDone = false;
        
            MarkAsAlive();
            mMinerThread = new System.Threading.Thread(MinerThread);
            mMinerThread.IsBackground = true;
            mMinerThread.Start();
        }

        unsafe protected virtual void MinerThread() { }

        public void Stop()
        {
            mStopped = true;
        }

        public void WaitForExit(int ms)
        {
            while (!Done && ms > 0)
            {
                System.Threading.Thread.Sleep((ms < 10) ? ms : 10);
                ms -= 10;
            }
        }

        public void Abort()
        {
            if (mMinerThread != null)
            {
                try
                {
                    mMinerThread.Abort();
                }
                catch (Exception ex) { }
                mMinerThread = null;
            }
        }

        protected void MarkAsAlive()
        {
            mLastAlive = DateTime.Now;
        }

        protected void MarkAsDone()
        {
            mDone = true;
        }

        public void KeepAlive()
        {
            if (mMinerThread != null && (DateTime.Now - mLastAlive).TotalSeconds >= 5)
                mSpeed = 0;
            if (mMinerThread != null && (DateTime.Now - mLastAlive).TotalSeconds >= 60)
            {
                MainForm.Logger("Miner thread is unresponsive. Restarting...");
                try
                {
                    mMinerThread.Abort();
                }
                catch (Exception) { }
                mSpeed = 0;
                Start();
            }
        }
    }
}
