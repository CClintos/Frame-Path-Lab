using System.Runtime.InteropServices;
using FramePathLab.Core.Models;

namespace FramePathLab.Windows.Scanning;

/// <summary>
/// Reads the driver profile that applies to the game.
///
/// This is the one settings surface the rest of the application is completely blind to. A player
/// can have every Windows and in-game setting correct and still be running the game against a
/// driver profile that lets the GPU drop performance states between frames, or that overrides the
/// in-game latency path. None of that is visible from any Windows API.
///
/// The driver's own library is loaded by name at runtime, exactly as the telemetry path does, so
/// no binary is bundled. Every call is read-only: the settings framework is opened, the profile is
/// read, and the session is destroyed without ever calling a save. Any unexpected status aborts
/// into "unavailable" rather than guessing at a value.
/// </summary>
public static class NvidiaProfileScanner
{
    private const int NvapiOk = 0;

    // Interface identifiers are how this library exposes its entry points; they are stable and
    // published in the vendor's own header.
    private const uint IdInitialize = 0x0150E828;
    private const uint IdUnload = 0xD22BDD7E;
    private const uint IdDrsCreateSession = 0x0694D52E;
    private const uint IdDrsDestroySession = 0xDAD9CFF8;
    private const uint IdDrsLoadSettings = 0x375DBD6B;
    private const uint IdDrsGetBaseProfile = 0xDA8466A0;
    private const uint IdDrsFindApplicationByName = 0xEEE566B2;
    private const uint IdDrsGetSetting = 0x73BF8338;

    private const uint SettingPreferredPstate = 0x1057EB71;
    private const uint SettingPrerenderLimit = 0x007BA09E;
    private const uint SettingVsyncMode = 0x00A879CF;
    private const uint SettingMaxFrameRate = 0x10835016;
    private const uint SettingThreadedOptimization = 0x20C1221E;
    private const uint SettingShaderCacheSize = 0x2E24C86D;

    public static NvidiaProfileState Scan(string gameExecutableName)
    {
        if (!NativeLibrary.TryLoad("nvapi64.dll", out var library))
        {
            return Unavailable("The NVIDIA driver library is not present on this system.");
        }

        try
        {
            if (!NativeLibrary.TryGetExport(library, "nvapi_QueryInterface", out var queryAddress))
            {
                return Unavailable("The driver library did not expose its interface table.");
            }

            var query = Marshal.GetDelegateForFunctionPointer<QueryInterface>(queryAddress);
            var initialize = Resolve<Initialize>(query, IdInitialize);
            var unload = Resolve<Unload>(query, IdUnload);
            if (initialize is null || initialize() != NvapiOk)
            {
                return Unavailable("The NVIDIA driver interface could not be initialised.");
            }

            try
            {
                return ReadProfile(query, gameExecutableName);
            }
            finally
            {
                unload?.Invoke();
            }
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException
                                              or BadImageFormatException or MarshalDirectiveException)
        {
            return Unavailable($"The NVIDIA driver interface could not be used: {exception.Message}");
        }
        finally
        {
            NativeLibrary.Free(library);
        }
    }

    private static NvidiaProfileState ReadProfile(QueryInterface query, string gameExecutableName)
    {
        var createSession = Resolve<DrsCreateSession>(query, IdDrsCreateSession);
        var destroySession = Resolve<DrsDestroySession>(query, IdDrsDestroySession);
        var loadSettings = Resolve<DrsLoadSettings>(query, IdDrsLoadSettings);
        var getBaseProfile = Resolve<DrsGetBaseProfile>(query, IdDrsGetBaseProfile);
        var findApplication = Resolve<DrsFindApplicationByName>(query, IdDrsFindApplicationByName);
        var getSetting = Resolve<DrsGetSetting>(query, IdDrsGetSetting);

        if (createSession is null || destroySession is null || loadSettings is null
            || getBaseProfile is null || getSetting is null)
        {
            return Unavailable("The driver settings interface is not available in this driver version.");
        }

        if (createSession(out var session) != NvapiOk)
        {
            return Unavailable("A driver settings session could not be created.");
        }

        try
        {
            if (loadSettings(session) != NvapiOk)
            {
                return Unavailable("Driver settings could not be loaded.");
            }

            // Prefer the profile that actually applies to the game executable; fall back to the
            // global base profile so the reading is still meaningful when no per-game profile
            // exists, and say which one was used.
            var profileName = $"{gameExecutableName}.exe";
            nint profile = 0;
            var usingApplicationProfile = false;

            if (findApplication is not null)
            {
                var application = new byte[NvdrsApplicationBytes];
                WriteVersion(application, NvdrsApplicationBytes, 3);
                if (findApplication(session, profileName, out var found, application) == NvapiOk && found != 0)
                {
                    profile = found;
                    usingApplicationProfile = true;
                }
            }

            if (profile == 0)
            {
                if (getBaseProfile(session, out profile) != NvapiOk || profile == 0)
                {
                    return Unavailable("Neither an application profile nor the base profile could be opened.");
                }
            }

            var settings = new List<NvidiaProfileSetting>();
            AddSetting(settings, getSetting, session, profile, SettingPreferredPstate,
                "Power management mode", DescribePowerMode, "Prefer maximum performance", value => value == 1);
            AddSetting(settings, getSetting, session, profile, SettingPrerenderLimit,
                "Low latency mode", DescribeLowLatency, "On or Ultra", value => value is >= 1 and <= 3);
            AddSetting(settings, getSetting, session, profile, SettingVsyncMode,
                "Vertical sync", DescribeVsync, "Use the 3D application setting", value => value == 0x60925292);
            AddSetting(settings, getSetting, session, profile, SettingMaxFrameRate,
                "Driver frame rate limit", value => value == 0 ? "Off" : $"{value} FPS",
                "Off, unless it is the chosen limiter", value => value == 0);
            AddSetting(settings, getSetting, session, profile, SettingThreadedOptimization,
                "Threaded optimization", DescribeThreaded, "Auto", value => value == 2);
            AddSetting(settings, getSetting, session, profile, SettingShaderCacheSize,
                "Shader cache size", DescribeShaderCache, "Unlimited", value => value == 0xFFFFFFFF);

            return settings.Count == 0
                ? Unavailable("The driver returned no readable settings for this profile.")
                : new NvidiaProfileState(
                    true,
                    usingApplicationProfile ? profileName : "Global base profile",
                    settings,
                    usingApplicationProfile
                        ? $"Read from the driver profile for {profileName}."
                        : $"No driver profile exists for {profileName}; the global base profile was read instead.");
        }
        finally
        {
            destroySession(session);
        }
    }

    private static void AddSetting(
        List<NvidiaProfileSetting> settings,
        DrsGetSetting getSetting,
        nint session,
        nint profile,
        uint settingId,
        string name,
        Func<uint, string> describe,
        string recommended,
        Func<uint, bool> isOptimal)
    {
        var buffer = new byte[NvdrsSettingBytes];
        WriteVersion(buffer, NvdrsSettingBytes, 1);
        if (getSetting(session, profile, settingId, buffer) != NvapiOk)
        {
            return;
        }

        var value = BitConverter.ToUInt32(buffer, CurrentValueOffset);
        settings.Add(new NvidiaProfileSetting(name, describe(value), recommended, isOptimal(value)));
    }

    private static string DescribePowerMode(uint value) => value switch
    {
        0 => "Adaptive",
        1 => "Prefer maximum performance",
        2 => "Driver controlled",
        3 => "Prefer consistent performance",
        _ => $"Optimal power ({value})"
    };

    private static string DescribeLowLatency(uint value) => value switch
    {
        0 => "Off (up to three frames queued)",
        1 => "On (one frame queued)",
        2 => "Two frames queued",
        3 => "Ultra",
        _ => $"Driver default ({value})"
    };

    private static string DescribeVsync(uint value) => value switch
    {
        0x60925292 => "Use the 3D application setting",
        0x08416747 => "Force off",
        0x47814940 => "Force on",
        0x99283165 => "Adaptive",
        _ => $"Driver value 0x{value:X}"
    };

    private static string DescribeThreaded(uint value) => value switch
    {
        0 => "Off",
        1 => "On",
        2 => "Auto",
        _ => $"Driver value {value}"
    };

    private static string DescribeShaderCache(uint value) => value switch
    {
        0 => "Disabled",
        0xFFFFFFFF => "Unlimited",
        _ => $"{value} MB"
    };

    // The settings structures are large and version-stamped. Rather than mirroring every field,
    // a generous zeroed buffer is allocated and only the version stamp and the current-value field
    // are touched; the driver validates the stamp and refuses anything it does not recognise, so a
    // mismatch degrades to "unavailable" instead of reading a wrong value.
    private const int NvdrsSettingBytes = 0x3020;
    private const int NvdrsApplicationBytes = 0x1090;
    private const int CurrentValueOffset = 0x102C;

    private static void WriteVersion(byte[] buffer, int structureBytes, int version)
    {
        var stamp = (uint)structureBytes | ((uint)version << 16);
        BitConverter.TryWriteBytes(buffer.AsSpan(0, 4), stamp);
    }

    private static TDelegate? Resolve<TDelegate>(QueryInterface query, uint id)
        where TDelegate : Delegate
    {
        var address = query(id);
        return address == 0 ? null : Marshal.GetDelegateForFunctionPointer<TDelegate>(address);
    }

    private static NvidiaProfileState Unavailable(string reason)
        => new(false, string.Empty, [], reason);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint QueryInterface(uint id);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Initialize();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Unload();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int DrsCreateSession(out nint session);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int DrsDestroySession(nint session);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int DrsLoadSettings(nint session);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int DrsGetBaseProfile(nint session, out nint profile);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private delegate int DrsFindApplicationByName(
        nint session,
        [MarshalAs(UnmanagedType.LPWStr)] string applicationName,
        out nint profile,
        [In, Out] byte[] application);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int DrsGetSetting(nint session, nint profile, uint settingId, [In, Out] byte[] setting);
}
