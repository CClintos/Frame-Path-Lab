using System.Diagnostics;
using System.Runtime.InteropServices;
using FramePathLab.Core.Models;
using FramePathLab.Windows.Input;

namespace FramePathLab.Windows.Benchmark;

/// <summary>
/// A deterministic frame-delivery benchmark the application runs itself.
///
/// Why this exists rather than a third-party benchmark: the tweaks being measured are mostly about
/// how a frame reaches the display and how punctually the thread producing it is scheduled. An
/// OpenGL timedemo cannot answer that — it goes through a different presentation path entirely, and
/// it reports average throughput, which is the one metric that hides the problem. Owning the swap
/// chain means presenting through the same path a modern game does, and timing it from inside the
/// process with no external collector and no events to lose.
///
/// Why this rather than asking for a real match: repeatability. "Play a round" is never the same
/// workload twice, so the difference between two runs contains the change under test plus whatever
/// else happened in those rounds. This does identical work every time, which is what makes a small
/// difference attributable at all.
///
/// What it is not: a substitute for the game. It exercises the presentation path, the scheduler and
/// the platform's power behaviour faithfully, because those are shared. It does not reproduce any
/// particular engine's memory access pattern, so a change that works through cache or memory
/// latency will under-report here. Those stay confirmed against a real capture.
/// </summary>
public sealed class SyntheticBenchmark
{
    private const string WindowClassName = "FramePathLabBenchmarkTarget";
    private const int Width = 640;
    private const int Height = 360;

    /// <summary>
    /// Discarded before measuring, so clock ramp and cache warmup are excluded. Timed rather than
    /// counted in frames: a fixed frame count is a fixed fraction of a fast machine's run and the
    /// entire budget of a slow one.
    /// </summary>
    private static readonly TimeSpan WarmupDuration = TimeSpan.FromSeconds(1.5);

    /// <summary>
    /// Below this the percentiles are too unstable to compare between runs. It has to clear the
    /// comparison's own minimum, or a run can complete and then be refused as incomparable — which
    /// is worse than failing outright, because it looks like a result.
    /// </summary>
    private const int MinimumMeasuredFrames = 3_500;

    public BenchmarkResult Run(BenchmarkOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        BenchmarkResult? result = null;
        Exception? failure = null;

        // The swap chain and its window must live on one thread with a message pump, and that
        // thread must not be the caller's.
        var thread = new Thread(() =>
        {
            try
            {
                result = Execute(options, cancellationToken);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        })
        {
            IsBackground = true,
            Name = "FramePath Lab benchmark"
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        if (!thread.Join(options.MaximumDuration + TimeSpan.FromSeconds(30)))
        {
            return BenchmarkResult.Failed("The benchmark did not complete within its time budget.");
        }

        return failure is not null
            ? BenchmarkResult.Failed($"The benchmark could not run: {failure.Message}")
            : result ?? BenchmarkResult.Failed("The benchmark produced no result.");
    }

    private static BenchmarkResult Execute(BenchmarkOptions options, CancellationToken cancellationToken)
    {
        nint window = 0;
        ushort classAtom = 0;
        WndProc? procedure = null;
        IDxgiSwapChain? swapChain = null;
        nint device = 0;
        nint context = 0;

        try
        {
            var instance = RawInputInterop.GetModuleHandle(null);
            procedure = RawInputInterop.DefWindowProc;

            var windowClass = new WndClassEx
            {
                Size = (uint)Marshal.SizeOf<WndClassEx>(),
                WndProc = Marshal.GetFunctionPointerForDelegate(procedure),
                Instance = instance,
                ClassName = WindowClassName
            };

            classAtom = RawInputInterop.RegisterClassEx(ref windowClass);
            if (classAtom == 0 && Marshal.GetLastWin32Error() != 1410)
            {
                return BenchmarkResult.Failed(
                    $"The benchmark window class could not be registered ({Marshal.GetLastWin32Error()}).");
            }

            // A real but hidden top-level window. It must be a genuine window for the swap chain to
            // take the flip path; a message-only window cannot present.
            window = RawInputInterop.CreateWindowEx(
                0, WindowClassName, "FramePath Lab benchmark",
                0x00800000, 0, 0, Width, Height, 0, 0, instance, 0);
            if (window == 0)
            {
                return BenchmarkResult.Failed(
                    $"The benchmark window could not be created ({Marshal.GetLastWin32Error()}).");
            }

            var description = new SwapChainDescription
            {
                BufferDescription = new DxgiModeDescription
                {
                    Width = Width,
                    Height = Height,
                    RefreshRate = new DxgiRational { Numerator = 0, Denominator = 1 },
                    Format = SwapChainInterop.FormatB8G8R8A8Unorm
                },
                SampleDescription = new DxgiSampleDescription { Count = 1, Quality = 0 },
                BufferUsage = SwapChainInterop.UsageRenderTargetOutput,
                BufferCount = 2,
                OutputWindow = window,
                Windowed = 1,
                SwapEffect = SwapChainInterop.SwapEffectFlipDiscard,
                Flags = options.AllowTearing ? SwapChainInterop.SwapChainFlagAllowTearing : 0
            };

            uint[] levels = [SwapChainInterop.FeatureLevel110];
            var created = SwapChainInterop.D3D11CreateDeviceAndSwapChain(
                0, SwapChainInterop.D3DDriverTypeHardware, 0, 0,
                levels, (uint)levels.Length, SwapChainInterop.D3DSdkVersion,
                ref description, out swapChain, out device, out _, out context);

            if (created < 0 || swapChain is null)
            {
                return BenchmarkResult.Failed(
                    $"A Direct3D swap chain could not be created (0x{created:X8}). "
                    + "A hardware graphics adapter is required.");
            }

            return Measure(swapChain, options, cancellationToken);
        }
        finally
        {
            if (swapChain is not null)
            {
                Marshal.ReleaseComObject(swapChain);
            }

            if (context != 0)
            {
                Marshal.Release(context);
            }

            if (device != 0)
            {
                Marshal.Release(device);
            }

            if (window != 0)
            {
                RawInputInterop.DestroyWindow(window);
            }

            GC.KeepAlive(procedure);
        }
    }

    private static BenchmarkResult Measure(
        IDxgiSwapChain swapChain,
        BenchmarkOptions options,
        CancellationToken cancellationToken)
    {
        var syncInterval = options.AllowTearing ? 0u : 1u;
        var presentFlags = options.AllowTearing ? SwapChainInterop.PresentAllowTearing : 0u;
        var workload = options.Workload ?? FrameWorkload.Create();

        var frameTimes = new List<double>(16384);
        var cpuTimes = new List<double>(16384);
        var deadline = Stopwatch.GetTimestamp();
        var previous = 0L;
        var frame = 0;
        var presentFailures = 0;

        swapChain.GetLastPresentCount(out var firstPresentCount);

        while (!cancellationToken.IsCancellationRequested)
        {
            var elapsed = Stopwatch.GetElapsedTime(deadline);
            var measuring = elapsed >= WarmupDuration;

            // Run until enough frames exist to compare, not merely until the clock runs out. A
            // duration alone produces plenty of frames on a fast machine and too few on a slow
            // one, and too few cannot be compared at all.
            if (measuring
                && frameTimes.Count >= options.TargetFrames
                && elapsed >= WarmupDuration + options.TotalDuration)
            {
                break;
            }

            if (elapsed >= WarmupDuration + options.MaximumDuration)
            {
                break;
            }

            var cpuStart = Stopwatch.GetTimestamp();
            SimulateFrameWork(workload);
            var cpuElapsed = Stopwatch.GetElapsedTime(cpuStart).TotalMilliseconds;

            var presented = swapChain.Present(syncInterval, presentFlags);
            if (presented < 0)
            {
                presentFailures++;
                if (presentFailures > 16)
                {
                    return BenchmarkResult.Failed($"Presentation failed repeatedly (0x{presented:X8}).");
                }
            }

            var now = Stopwatch.GetTimestamp();
            if (previous != 0 && measuring)
            {
                var frameMs = Stopwatch.GetElapsedTime(previous, now).TotalMilliseconds;

                // A frame beyond a quarter second is the machine being interrupted by something
                // outside the benchmark, not a property of the configuration under test.
                if (frameMs is > 0 and < 250)
                {
                    frameTimes.Add(frameMs);
                    cpuTimes.Add(cpuElapsed);
                }
            }

            previous = now;
            frame++;
        }

        if (frameTimes.Count < MinimumMeasuredFrames)
        {
            return BenchmarkResult.Failed(
                $"Only {frameTimes.Count} frames were measured; at least {MinimumMeasuredFrames} are needed "
                + "for the percentiles to be comparable. Use a longer run.");
        }

        swapChain.GetLastPresentCount(out var lastPresentCount);
        var statistics = swapChain.GetFrameStatistics(out var stats) >= 0 ? stats : default;

        return BenchmarkResult.Completed(
            frameTimes,
            cpuTimes,
            lastPresentCount - firstPresentCount,
            statistics.PresentCount,
            statistics.PresentRefreshCount,
            options.AllowTearing,
            workload);
    }

    /// <summary>
    /// Per-frame work shaped like a competitive shooter's, not like a synthetic throughput loop.
    ///
    /// The shape matters more than the amount. A first-person engine's frame is dominated by
    /// walking entity and scene structures — dependent loads whose addresses are only known once
    /// the previous load returns. That work is bound by memory latency, not by arithmetic
    /// throughput or bandwidth, which is why cache capacity moves it so much and why a processor
    /// carrying stacked cache pulls ahead in exactly this kind of title.
    ///
    /// A tight arithmetic loop over a cache-resident array measures none of that. It never misses,
    /// so it never responds to cache size, memory speed or fabric ratio, and it would report those
    /// changes as doing nothing.
    ///
    /// So the frame is a pointer chase over a working set deliberately larger than mid-level cache:
    /// a random permutation cycle, where each step's address comes from the previous step's value.
    /// Prefetchers cannot run ahead of it, and the cost per step is a cache or memory access. A
    /// smaller arithmetic component rides alongside so the frame is not purely stalled, and part of
    /// the work is dispatched across threads so core availability and scheduling policy register
    /// the way an engine's job system would.
    /// </summary>
    private static long SimulateFrameWork(FrameWorkload workload)
    {
        var set = workload.WorkingSet;
        var mask = set.Length - 1;

        // The main thread carries the largest share, mirroring an engine whose frame is gated on
        // one dominant thread rather than spread evenly.
        var index = workload.Seed & mask;
        long accumulator = 0;
        for (var step = 0; step < workload.MainThreadChase; step++)
        {
            index = set[index];
            accumulator += index;
        }

        if (workload.WorkerThreads > 1 && workload.WorkerChase > 0)
        {
            var partials = new long[workload.WorkerThreads];
            Parallel.For(0, workload.WorkerThreads, worker =>
            {
                // Each worker starts at a different point in the cycle so they miss independently
                // rather than sharing one warmed path.
                var local = (workload.Seed + (worker * 7919)) & mask;
                long sum = 0;
                for (var step = 0; step < workload.WorkerChase; step++)
                {
                    local = set[local];
                    sum += local;
                }

                partials[worker] = sum;
            });

            for (var worker = 0; worker < partials.Length; worker++)
            {
                accumulator += partials[worker];
            }
        }

        // A little arithmetic so the frame is not purely memory-stalled, which would over-weight
        // memory tuning relative to clock and scheduling behaviour.
        var scalar = 1.0;
        for (var step = 1; step <= workload.ArithmeticSteps; step++)
        {
            scalar += Math.Sqrt(scalar + step) / (step + scalar);
        }

        return accumulator + (long)scalar;
    }
}

/// <summary>
/// The per-frame workload. Fixed rather than calibrated: a benchmark that adjusts its work until
/// every machine reports the same frame time cannot compare machines, and worse, it would quietly
/// compensate for a change that made the processor slower. The work is constant, so a faster
/// configuration finishes it sooner and scores higher — which is the whole point.
/// </summary>
/// <param name="WorkingSet">
/// A random permutation cycle. Sized above mid-level cache so the chase actually misses, which is
/// what makes cache capacity, memory speed and fabric ratio visible in the result.
/// </param>
public sealed record FrameWorkload(
    int[] WorkingSet,
    int MainThreadChase,
    int WorkerThreads,
    int WorkerChase,
    int ArithmeticSteps,
    int Seed)
{
    /// <summary>
    /// Twenty-four mebibytes of pointers. Chosen to sit above the last-level cache of a
    /// conventional desktop processor while fitting inside one carrying stacked cache — so the
    /// difference between those two shows up rather than being averaged away.
    /// </summary>
    public const int DefaultWorkingSetBytes = 24 * 1024 * 1024;

    public double WorkingSetMiB => WorkingSet.Length * sizeof(int) / 1024d / 1024d;

    public static FrameWorkload Create(int workingSetBytes = DefaultWorkingSetBytes, int? workerThreads = null)
    {
        // A power-of-two length lets the index wrap with a mask instead of a division, so the
        // measured cost stays the memory access rather than the arithmetic around it.
        var entries = 1;
        while (entries * sizeof(int) < workingSetBytes && entries < 1 << 26)
        {
            entries <<= 1;
        }

        var set = BuildPermutationCycle(entries);
        var workers = workerThreads ?? Math.Clamp(Environment.ProcessorCount / 2, 1, 8);

        // Sized so a current desktop lands in the few-hundred frames-per-second range a
        // competitive title actually occupies, and a thin mobile part lands well below it. The
        // work is constant, so that spread is the machines differing rather than the benchmark
        // adjusting to them.
        return new FrameWorkload(
            set,
            MainThreadChase: 90_000,
            WorkerThreads: workers,
            WorkerChase: 20_000,
            ArithmeticSteps: 8_000,
            Seed: 12_345);
    }

    /// <summary>
    /// Builds a single cycle covering every entry, so the chase can never fall into a short loop
    /// that would sit in cache and stop missing.
    /// </summary>
    private static int[] BuildPermutationCycle(int entries)
    {
        var order = new int[entries];
        for (var index = 0; index < entries; index++)
        {
            order[index] = index;
        }

        // Deterministic shuffle: the workload has to be identical on every run for two results to
        // be comparable at all.
        var random = new Random(20260827);
        for (var index = entries - 1; index > 0; index--)
        {
            var swap = random.Next(index + 1);
            (order[index], order[swap]) = (order[swap], order[index]);
        }

        var cycle = new int[entries];
        for (var index = 0; index < entries - 1; index++)
        {
            cycle[order[index]] = order[index + 1];
        }

        cycle[order[entries - 1]] = order[0];
        return cycle;
    }
}

/// <summary>How the benchmark should be run.</summary>
/// <param name="TotalDuration">Minimum measured time, once the frame target is also met.</param>
/// <param name="TargetFrames">
/// Frames needed before a run may finish. This is the binding constraint on a slow machine, and it
/// is what keeps a result comparable rather than merely complete.
/// </param>
/// <param name="MaximumDuration">Hard cap, so a very slow machine still terminates.</param>
public sealed record BenchmarkOptions(
    TimeSpan TotalDuration,
    bool AllowTearing,
    FrameWorkload? Workload = null,
    int TargetFrames = 4_000,
    TimeSpan MaximumDuration = default)
{
    public TimeSpan MaximumDuration { get; init; } =
        MaximumDuration == default ? TimeSpan.FromSeconds(120) : MaximumDuration;

    /// <summary>
    /// Unsynchronised and processor-bound, which is how a competitive title is actually run: the
    /// frame rate sits well above the refresh rate and the processor is the limiting stage.
    /// </summary>
    public static BenchmarkOptions Default { get; } = new(TimeSpan.FromSeconds(15), true);

    /// <summary>A shorter pass for checking the harness works before spending a full run.</summary>
    public static BenchmarkOptions Quick { get; } = new(TimeSpan.FromSeconds(4), true, TargetFrames: 3_500);
}

/// <summary>The measured outcome of one benchmark run.</summary>
public sealed record BenchmarkResult(
    bool Succeeded,
    IReadOnlyList<double> FrameTimes,
    IReadOnlyList<double> CpuTimes,
    uint PresentsIssued,
    uint PresentsCompleted,
    uint PresentRefreshCount,
    bool TearingAllowed,
    string Observation,
    FrameWorkload? Workload = null)
{
    public static BenchmarkResult Failed(string reason)
        => new(false, [], [], 0, 0, 0, false, reason);

    public static BenchmarkResult Completed(
        IReadOnlyList<double> frameTimes,
        IReadOnlyList<double> cpuTimes,
        uint presentsIssued,
        uint presentsCompleted,
        uint presentRefreshCount,
        bool tearingAllowed,
        FrameWorkload workload)
        => new(true, frameTimes, cpuTimes, presentsIssued, presentsCompleted, presentRefreshCount,
            tearingAllowed,
            $"{frameTimes.Count:N0} frames measured over a {workload.WorkingSetMiB:0.#} MiB working set "
            + $"with {workload.WorkerThreads} worker thread(s)"
            + (tearingAllowed ? ", unsynchronised." : ", synchronised to the display."),
            workload);
}
