using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace SoundFlip
{
    class AudioDevice
    {
        public string Id;
        public string Name;
    }

    // Минимальная обёртка над Windows Core Audio (MMDevice API) + недокументированный IPolicyConfig,
    // которым сама Windows переключает устройство по умолчанию.
    static class Audio
    {
        const int eRender = 0;
        const int DEVICE_STATE_ACTIVE = 1;

        public static List<AudioDevice> GetOutputs()
        {
            var list = new List<AudioDevice>();
            var en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            try
            {
                IMMDeviceCollection col;
                if (en.EnumAudioEndpoints(eRender, DEVICE_STATE_ACTIVE, out col) != 0) return list;
                uint n;
                col.GetCount(out n);
                for (uint i = 0; i < n; i++)
                {
                    IMMDevice d;
                    if (col.Item(i, out d) != 0) continue;
                    string id;
                    d.GetId(out id);
                    list.Add(new AudioDevice { Id = id, Name = GetName(d) ?? id });
                    Marshal.ReleaseComObject(d);
                }
                Marshal.ReleaseComObject(col);
            }
            finally { Marshal.ReleaseComObject(en); }
            return list;
        }

        public static string GetDefaultId()
        {
            var en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            try
            {
                IMMDevice d;
                if (en.GetDefaultAudioEndpoint(eRender, 1 /*eMultimedia*/, out d) != 0 || d == null) return null;
                string id;
                d.GetId(out id);
                Marshal.ReleaseComObject(d);
                return id;
            }
            catch { return null; }
            finally { Marshal.ReleaseComObject(en); }
        }

        public static void SetDefault(string id)
        {
            var pc = (IPolicyConfig)new PolicyConfigCom();
            try
            {
                for (int role = 0; role < 3; role++) // eConsole, eMultimedia, eCommunications
                    pc.SetDefaultEndpoint(id, role);
            }
            finally { Marshal.ReleaseComObject(pc); }
        }

        static string GetName(IMMDevice d)
        {
            IPropertyStore ps;
            if (d.OpenPropertyStore(0 /*STGM_READ*/, out ps) != 0) return null;
            try
            {
                var key = new PROPERTYKEY { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 14 };
                PROPVARIANT pv;
                if (ps.GetValue(ref key, out pv) != 0) return null;
                string s = pv.vt == 31 /*VT_LPWSTR*/ ? Marshal.PtrToStringUni(pv.p) : null;
                PropVariantClear(ref pv);
                return s;
            }
            finally { Marshal.ReleaseComObject(ps); }
        }

        [DllImport("ole32.dll")]
        static extern int PropVariantClear(ref PROPVARIANT pv);

        [StructLayout(LayoutKind.Sequential)]
        struct PROPERTYKEY { public Guid fmtid; public int pid; }

        [StructLayout(LayoutKind.Sequential)]
        struct PROPVARIANT { public ushort vt; public ushort r1, r2, r3; public IntPtr p; public IntPtr p2; }

        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        class MMDeviceEnumeratorCom { }

        [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
        class PolicyConfigCom { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
            [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
            [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
            [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
        }

        [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceCollection
        {
            [PreserveSig] int GetCount(out uint count);
            [PreserveSig] int Item(uint index, out IMMDevice device);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDevice
        {
            [PreserveSig] int Activate(IntPtr iid, int clsCtx, IntPtr activationParams, IntPtr iface);
            [PreserveSig] int OpenPropertyStore(int stgmAccess, out IPropertyStore properties);
            [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
            [PreserveSig] int GetState(out int state);
        }

        [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IPropertyStore
        {
            [PreserveSig] int GetCount(out int count);
            [PreserveSig] int GetAt(int index, out PROPERTYKEY key);
            [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
            [PreserveSig] int SetValue(ref PROPERTYKEY key, ref PROPVARIANT value);
            [PreserveSig] int Commit();
        }

        // Порядок методов важен (vtable), неиспользуемые объявлены с заглушками.
        [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IPolicyConfig
        {
            [PreserveSig] int GetMixFormat(IntPtr a, IntPtr b);
            [PreserveSig] int GetDeviceFormat(IntPtr a, int b, IntPtr c);
            [PreserveSig] int ResetDeviceFormat(IntPtr a);
            [PreserveSig] int SetDeviceFormat(IntPtr a, IntPtr b, IntPtr c);
            [PreserveSig] int GetProcessingPeriod(IntPtr a, int b, IntPtr c, IntPtr d);
            [PreserveSig] int SetProcessingPeriod(IntPtr a, IntPtr b);
            [PreserveSig] int GetShareMode(IntPtr a, IntPtr b);
            [PreserveSig] int SetShareMode(IntPtr a, IntPtr b);
            [PreserveSig] int GetPropertyValue(IntPtr a, IntPtr b, IntPtr c);
            [PreserveSig] int SetPropertyValue(IntPtr a, IntPtr b, IntPtr c);
            [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
            [PreserveSig] int SetEndpointVisibility(IntPtr a, int b);
        }
    }
}
