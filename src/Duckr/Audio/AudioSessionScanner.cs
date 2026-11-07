/*
 *  Duckr
 *  Licensed under GNU GPL v3. See LICENSE.
 *
 *  Lists every audio session on every active output device, with its volume
 *  control and its peak meter. Many tools only look at the
 *  default device and keeps one session per process; creators often have an
 *  audio interface plus headphones, and browsers open many sessions, so
 *  Duckr needs the full picture.
 *
 *  Only Windows Core Audio COM calls are used. No network, no files.
 */

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Duckr
{
    public sealed class AudioSession
    {
        internal AudioSession(int pid, string processName, string instanceId, bool isSystemSounds,
            ICmSimpleAudioVolume volume, ICmAudioMeterInformation? meter, object control)
        {
            Pid = pid;
            ProcessName = processName;
            InstanceId = instanceId;
            IsSystemSounds = isSystemSounds;
            m_volume = volume;
            m_meter = meter;
            m_control = control;
        }

        public int Pid { get; }
        public string ProcessName { get; }
        /// <summary>Unique per session, stable while the app keeps it open.</summary>
        public string InstanceId { get; }
        public bool IsSystemSounds { get; }

        private readonly ICmSimpleAudioVolume m_volume;
        private readonly ICmAudioMeterInformation? m_meter;
        private readonly object m_control;

        public float Peak
        {
            get
            {
                if (m_meter == null) return 0f;
                try { return m_meter.GetPeakValue(out float p) == 0 ? p : 0f; }
                catch { return 0f; }
            }
        }

        public float? Volume
        {
            get
            {
                try { return m_volume.GetMasterVolume(out float v) == 0 ? v : null; }
                catch { return null; }
            }
        }

        public bool SetVolume(float level)
        {
            try
            {
                // our own event context, so other mixers can tell it was us
                Guid ctx = DuckEngine.EventContext;
                return m_volume.SetMasterVolume(Math.Clamp(level, 0f, 1f), ref ctx) == 0;
            }
            catch { return false; }
        }

        // No Marshal.ReleaseComObject on purpose. .NET keeps ONE wrapper per COM
        // object for the whole app, so releasing a wrapper by hand can silently break
        // any other code holding the same object. The garbage collector frees them.
        internal object Control => m_control;
    }

    public sealed class AudioSessionScanner : IDisposable
    {
        private const int DEVICE_STATE_ACTIVE = 0x1;
        private const int AudioSessionStateExpired = 2;

        private static readonly Guid CLSID_MMDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");

        private ICmMMDeviceEnumerator? m_enumerator;
        private readonly List<AudioSession> m_sessions = new();
        private readonly Dictionary<int, string> m_nameCache = new();

        public IReadOnlyList<AudioSession> Sessions => m_sessions;

        public void Rescan()
        {
            ReleaseSessions();

            // Created from the CLSID, not from a [ComImport] class: if two [ComImport]
            // classes share a CLSID, .NET hands out the first one and casts fail.
            m_enumerator ??= (ICmMMDeviceEnumerator)Activator.CreateInstance(
                Type.GetTypeFromCLSID(CLSID_MMDeviceEnumerator, throwOnError: true)!)!;

            int hr = m_enumerator.EnumAudioEndpoints(CmEDataFlow.eRender, DEVICE_STATE_ACTIVE, out var devices);
            if (hr != 0 || devices == null)
            {
                LastError = $"EnumAudioEndpoints failed 0x{hr:X8}";
                LastDeviceCount = 0;
                return;
            }
            LastError = "";

            var seenPids = new HashSet<int>();
            uint deviceCount = 0;
            try
            {
                devices.GetCount(out uint count);
                deviceCount = count;
                for (uint d = 0; d < count; d++)
                {
                    if (devices.Item(d, out var device) != 0 || device == null)
                        continue;
                    try
                    {
                        ScanDevice(device, seenPids);
                    }
                    catch (Exception ex)
                    {
                        DuckrLog.Write($"[!] Duckr: skipped an audio device ({ex.Message})");
                    }
                    finally { }
                }
            }
            finally { LastDeviceCount = (int)deviceCount; }

            // forget names of processes that are gone (pids get reused)
            foreach (var pid in m_nameCache.Keys.Where(p => !seenPids.Contains(p)).ToList())
                m_nameCache.Remove(pid);
        }

        private void ScanDevice(ICmMMDevice device, HashSet<int> seenPids)
        {
            Guid iid = typeof(ICmAudioSessionManager2).GUID;
            int hr = device.Activate(ref iid, 0x17 /* CLSCTX_ALL */, IntPtr.Zero, out object o);
            if (hr != 0 || o == null)
            {
                LastError = $"Activate(SessionManager2) failed 0x{hr:X8}";
                return;
            }

            var mgr = (ICmAudioSessionManager2)o;
            ICmAudioSessionEnumerator? sessions = null;
            try
            {
                int hr2 = mgr.GetSessionEnumerator(out sessions);
                if (hr2 != 0 || sessions == null)
                {
                    LastError = $"GetSessionEnumerator failed 0x{hr2:X8}";
                    return;
                }

                sessions.GetCount(out int n);
                for (int i = 0; i < n; i++)
                {
                    if (sessions.GetSession(i, out var ctl) != 0 || ctl == null)
                        continue;

                    try
                    {
                        if (ctl.GetState(out int state) == 0 && state == AudioSessionStateExpired)
                            continue;

                        ctl.GetProcessId(out int pid);
                        bool isSystem = ctl.IsSystemSoundsSession() == 0;
                        ctl.GetSessionInstanceIdentifier(out string? instanceId);

                        if (ctl is not ICmSimpleAudioVolume vol)
                            continue;
                        var meter = ctl as ICmAudioMeterInformation;

                        string name = isSystem ? "System Sounds" : GetProcessName(pid);
                        seenPids.Add(pid);

                        m_sessions.Add(new AudioSession(pid, name, instanceId ?? $"{pid}:{i}", isSystem, vol, meter, ctl));
                    }
                    catch (Exception ex)
                    {
                        LastError = $"session {i}: {ex.GetType().Name} {ex.Message}";
                    }
                }
            }
            finally { }
        }

        public string GetProcessName(int pid)
        {
            if (pid <= 0) return "";
            if (m_nameCache.TryGetValue(pid, out var cached)) return cached;
            string name = "";
            try
            {
                using var p = Process.GetProcessById(pid);
                name = p.ProcessName;
            }
            catch { /* exited */ }
            m_nameCache[pid] = name;
            return name;
        }

        /// <summary>Number of active output devices found by the last scan.</summary>
        public int LastDeviceCount { get; private set; }

        /// <summary>Last problem seen while scanning, empty when fine.</summary>
        public string LastError { get; private set; } = "";

        private void ReleaseSessions() => m_sessions.Clear();

        public void Dispose()
        {
            ReleaseSessions();
            m_enumerator = null;
        }
    }

    // ------------------------------------------------------------------------
    // Core Audio COM interfaces. Full vtables in order, so every slot we call
    // lines up. Named Cm* (Core audio Media) to keep them apart from any other declarations.
    // ------------------------------------------------------------------------


    internal enum CmEDataFlow { eRender, eCapture, eAll }

    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ICmMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(CmEDataFlow dataFlow, int stateMask, out ICmMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(CmEDataFlow dataFlow, int role, out ICmMMDevice device);
        // GetDevice, RegisterEndpointNotificationCallback, UnregisterEndpointNotificationCallback not used
    }

    [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ICmMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out ICmMMDevice device);
    }

    [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ICmMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        // OpenPropertyStore, GetId, GetState not used
    }

    [Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ICmAudioSessionManager2
    {
        [PreserveSig] int GetAudioSessionControl(IntPtr sessionGuid, int streamFlags, out IntPtr control);
        [PreserveSig] int GetSimpleAudioVolume(IntPtr sessionGuid, int streamFlags, out IntPtr volume);
        [PreserveSig] int GetSessionEnumerator(out ICmAudioSessionEnumerator sessionEnum);
        // notification methods not used
    }

    [Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ICmAudioSessionEnumerator
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetSession(int index, out ICmAudioSessionControl2 session);
    }

    [Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ICmAudioSessionControl2
    {
        // IAudioSessionControl
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid ctx);
        [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
        [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid ctx);
        [PreserveSig] int GetGroupingParam(out Guid param);
        [PreserveSig] int SetGroupingParam(ref Guid param, ref Guid ctx);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr client);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr client);
        // IAudioSessionControl2
        [PreserveSig] int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetProcessId(out int pid);
        [PreserveSig] int IsSystemSoundsSession();
        [PreserveSig] int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
    }

    [Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ICmSimpleAudioVolume
    {
        [PreserveSig] int SetMasterVolume(float level, ref Guid ctx);
        [PreserveSig] int GetMasterVolume(out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    [Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ICmAudioMeterInformation
    {
        [PreserveSig] int GetPeakValue(out float peak);
        // GetMeteringChannelCount, GetChannelsPeakValues, QueryHardwareSupport not used
    }
}
