using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Cloo;



namespace GatelessGateSharp
{
    class Device
    {
        private int mDeviceIndex;
        private ComputeDevice mComputeDevice;
        private int mAcceptedShares;
        private int mRejectedShares;
        private String mName;
        private ComputeContext mContext = null;
        private System.Threading.Mutex mMutex = new System.Threading.Mutex();
        private List<ComputeDevice> mDeviceList;


        public String Vendor
        {
            get
            {
                return (mComputeDevice.Vendor == "Advanced Micro Devices, Inc.") ? "AMD" :
                       (mComputeDevice.Vendor == "NVIDIA Corporation") ? "NVIDIA" :
                       (mComputeDevice.Vendor == "Intel Corporation") ? "Intel" :
                       (mComputeDevice.Vendor == "GenuineIntel") ? "Intel" :
                       mComputeDevice.Vendor;
            }
        }

        public String Name { get { return mName; } }
        public List<ComputeDevice> DeviceList { get { return mDeviceList; } }

        public ComputeContext Context
        {
            get
            {
                mMutex.WaitOne();
                if (mContext == null)
                {
                    mDeviceList = new List<ComputeDevice>();
                    mDeviceList.Add(mComputeDevice);
                    var contextProperties = new ComputeContextPropertyList(mComputeDevice.Platform);
                    mContext = new ComputeContext(mDeviceList, contextProperties, null, IntPtr.Zero);
                }
                mMutex.ReleaseMutex();
                return mContext;
            }
        }

        public int DeviceIndex { get { return mDeviceIndex; } }
        public int AcceptedShares { get { return mAcceptedShares; } }
        public int RejectedShares { get { return mRejectedShares; } }
        public long MaxComputeUnits { get { return mComputeDevice.MaxComputeUnits; } }
        public ComputeDevice GetComputeDevice() { return mComputeDevice; }

        public Device(int aDeviceIndex, ComputeDevice aComputeDevice)
        {
            mComputeDevice = aComputeDevice;
            mDeviceIndex = aDeviceIndex;
            mAcceptedShares = 0;
            mRejectedShares = 0;
            mName = aComputeDevice.Name;

        }

        public ComputeDevice GetNewComputeDevice()
        {
            var computeDeviceArrayList = new ArrayList();
            foreach (var platform in ComputePlatform.Platforms)
            {
                IList<ComputeDevice> openclDevices = platform.Devices;
                var properties = new ComputeContextPropertyList(platform);
                using (var context = new ComputeContext(openclDevices, properties, null, IntPtr.Zero))
                {
                    foreach (var openclDevice in context.Devices)
                    {
                        if (IsOpenCLDeviceIgnored(openclDevice))
                            continue;
                        computeDeviceArrayList.Add(openclDevice);
                    }
                }
            }
            return (ComputeDevice)computeDeviceArrayList[mDeviceIndex];
        }

        public void SetADLName(String aName)
        {
            aName = aName.Replace("AMD ", "");
            aName = aName.Replace("(TM)", "");
            aName = aName.Replace(" Series", "");
            aName = aName.Replace(" Graphics", "");
            aName = aName.Replace("  ", " ");
            if (aName == "Radeon R9 Fury" && mComputeDevice.MaxComputeUnits == 64) // TODO
                aName = "Radeon R9 Fury X/Nano";
            mName = aName;
        }

        public int IncrementAcceptedShares()
        {
            return ++mAcceptedShares;
        }

        public int IncrementRejectedShares()
        {
            return ++mRejectedShares;
        }

        public void ClearShares()
        {
            mAcceptedShares = 0;
            mRejectedShares = 0;
        }

        public static bool IsOpenCLDeviceIgnored(ComputeDevice device)
        {
            return Regex.Match(device.Name, "Intel").Success || Regex.Match(device.Vendor, "Intel").Success || device.Type == ComputeDeviceTypes.Cpu;
        }

        public static Device[] GetAllDevices()
        {
            var computeDeviceArrayList = new ArrayList();

            foreach (var platform in ComputePlatform.Platforms)
            {
                IList<ComputeDevice> openclDevices = platform.Devices;
                var properties = new ComputeContextPropertyList(platform);
                using (var context = new ComputeContext(openclDevices, properties, null, IntPtr.Zero))
                {
                    foreach (var openclDevice in context.Devices)
                    {
                        if (IsOpenCLDeviceIgnored(openclDevice))
                            continue;
                        computeDeviceArrayList.Add(openclDevice);
                    }
                }

            }
            var computeDevices = Array.ConvertAll(computeDeviceArrayList.ToArray(), item => (ComputeDevice)item);
            Device[] devices = new Device[computeDevices.Length];
            var deviceIndex = 0;
            foreach (var computeDevice in computeDevices)
            {
                devices[deviceIndex] = new Device(deviceIndex, computeDevice);
                deviceIndex++;
            }

            return devices;
        }
    }
}
