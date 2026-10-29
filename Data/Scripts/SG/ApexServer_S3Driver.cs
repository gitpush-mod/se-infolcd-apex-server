// S3 spike (se-infolcd-apex-server#6). Throwaway: lives on a spike branch, never merged as-is.
// Runs the existing InfoLCD apps from a server-side session component instead of the screen's
// Script menu. Block name tags:
//   [ApexServer]          Components on every screen of the block, at the app's own rate
//   [ApexServer:Cargo]    another app (any name in Apps below)
//   [ApexServer:Cargo:5]  ...redrawn every 5 seconds instead (1 to 600)
//   [ApexServer:5]        Components every 5 seconds
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;
using IngameSurfaceProvider = Sandbox.ModAPI.Ingame.IMyTextSurfaceProvider;

namespace MahrianeIndustries.LCDInfo
{
    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public class ApexServerS3Driver : MySessionComponentBase
    {
        // True only while this driver is inside an app's Run(). The apps' dedicated-server early
        // return checks it, so screens set up through the Script menu behave exactly as before.
        public static bool Driving;

        const string Tag = "[ApexServer";
        const string DefaultApp = "Components";
        const int ScanEveryTicks = 300;
        const int ReportEveryTicks = 3600;
        const int MaxRedrawSeconds = 600;

        static readonly Dictionary<string, Func<IMyTextSurface, IMyCubeBlock, Vector2, MyTextSurfaceScriptBase>> Apps =
            new Dictionary<string, Func<IMyTextSurface, IMyCubeBlock, Vector2, MyTextSurfaceScriptBase>>(StringComparer.OrdinalIgnoreCase)
        {
            { "AirlockMonitor", (s, b, z) => new LCDAirlockMonitorSummary(s, b, z) },
            { "Ammo", (s, b, z) => new LCDAmmoSummaryInfo(s, b, z) },
            { "Cargo", (s, b, z) => new LCDCargoSummaryInfo(s, b, z) },
            { "Components", (s, b, z) => new LCDComponentsSummaryInfo(s, b, z) },
            { "Container", (s, b, z) => new LCDContainerSummaryInfo(s, b, z) },
            { "DamageMonitor", (s, b, z) => new LCDDamageMonitorSummary(s, b, z) },
            { "DetailedInfo", (s, b, z) => new LCDDetailedInfo(s, b, z) },
            { "DoorMonitor", (s, b, z) => new LCDDoorMonitorSummary(s, b, z) },
            { "Farming", (s, b, z) => new LCDFarmingSummaryInfo(s, b, z) },
            { "GasProduction", (s, b, z) => new LCDGasGenerationSummary(s, b, z) },
            { "GridInfo", (s, b, z) => new LCDGridInfoSummary(s, b, z) },
            { "Ingots", (s, b, z) => new LCDIngotsSummaryInfo(s, b, z) },
            { "Items", (s, b, z) => new LCDItemsSummaryInfo(s, b, z) },
            { "LifeSupport", (s, b, z) => new LCDLifeSupportSummary(s, b, z) },
            { "Ores", (s, b, z) => new LCDOresSummaryInfo(s, b, z) },
            { "Power", (s, b, z) => new LCDPowerSummary(s, b, z) },
            { "Production", (s, b, z) => new LCDProductionSummary(s, b, z) },
            { "Systems", (s, b, z) => new LCDSystemsSummary(s, b, z) },
            { "Weapons", (s, b, z) => new LCDWeaponsSummaryInfo(s, b, z) },
        };

        class Screen { public int Index; public IMyTextSurface Surface; public MyTextSurfaceScriptBase App; public int NextRunTick; public bool Failed; }
        class TaggedBlock { public IMyTerminalBlock Block; public string AppName; public int Seconds; public int RedrawTicks; public bool Seen; public readonly List<Screen> Screens = new List<Screen>(); }

        readonly Dictionary<long, TaggedBlock> _tagged = new Dictionary<long, TaggedBlock>();
        readonly HashSet<IMyEntity> _grids = new HashSet<IMyEntity>();
        readonly List<IMySlimBlock> _slims = new List<IMySlimBlock>();
        readonly List<long> _gone = new List<long>();
        readonly Stopwatch _watch = new Stopwatch();
        int _tick, _runs;
        double _totalMs, _maxMs;
        string _maxWhere;
        bool _started, _isServer;

        public override void UpdateAfterSimulation()
        {
            if (!_started)
            {
                _started = true;
                _isServer = MyAPIGateway.Multiplayer.IsServer;
                Log(_isServer ? "server: running InfoLCD apps on blocks tagged [ApexServer]" : "client: idle (IsServer=false)");
            }
            if (!_isServer) return;

            _tick++;
            if (_tick % ScanEveryTicks == 1) Scan();

            foreach (var t in _tagged.Values)
            {
                if (t.Block.Closed || t.Block.MarkedForClose) continue;
                foreach (var s in t.Screens)
                {
                    if (s.Failed || _tick < s.NextRunTick) continue;
                    s.NextRunTick = _tick + t.RedrawTicks;
                    Run(t, s);
                }
            }

            if (_tick % ReportEveryTicks == 0) Report();
        }

        protected override void UnloadData()
        {
            foreach (var t in _tagged.Values) Drop(t, false);
            _tagged.Clear(); _grids.Clear(); _slims.Clear(); _gone.Clear();
            Driving = false;
        }

        void Scan()
        {
            foreach (var t in _tagged.Values) t.Seen = false;

            _grids.Clear();
            MyAPIGateway.Entities.GetEntities(_grids, e => e is IMyCubeGrid);
            foreach (var grid in _grids)
            {
                _slims.Clear();
                ((IMyCubeGrid)grid).GetBlocks(_slims, b => b.FatBlock is IngameSurfaceProvider && b.FatBlock is IMyTerminalBlock);
                foreach (var slim in _slims) Track((IMyTerminalBlock)slim.FatBlock);
            }

            _gone.Clear();
            foreach (var pair in _tagged) if (!pair.Value.Seen) _gone.Add(pair.Key);
            foreach (var id in _gone)
            {
                var t = _tagged[id];
                Log($"'{t.Block.CustomName}': tag removed or block gone, {t.AppName} stopped");
                Drop(t, true);
                _tagged.Remove(id);
            }
        }

        void Track(IMyTerminalBlock block)
        {
            string appName;
            int seconds;
            if (!ParseTag(block.CustomName, out appName, out seconds)) return;

            TaggedBlock t;
            if (_tagged.TryGetValue(block.EntityId, out t))
            {
                t.Seen = true;
                if (string.Equals(t.AppName, appName, StringComparison.OrdinalIgnoreCase))
                {
                    if (t.Seconds != seconds)
                    {
                        Schedule(t, seconds);
                        Log($"'{block.CustomName}': {t.AppName} now {Describe(t)}");
                    }
                    return;
                }
                Log($"'{block.CustomName}': app changed from {t.AppName} to {appName}");
                Drop(t, false);
            }

            t = new TaggedBlock { Block = block, AppName = appName, Seen = true };
            _tagged[block.EntityId] = t;

            Func<IMyTextSurface, IMyCubeBlock, Vector2, MyTextSurfaceScriptBase> make;
            if (!Apps.TryGetValue(appName, out make))
            {
                Log($"'{block.CustomName}': no app called '{appName}'. Names: {string.Join(", ", Apps.Keys)}");
                return;
            }

            var provider = (IngameSurfaceProvider)block;
            for (int i = 0; i < provider.SurfaceCount; i++)
            {
                var surface = provider.GetSurface(i) as IMyTextSurface;
                if (surface == null) continue;
                try
                {
                    t.Screens.Add(new Screen { Index = i, Surface = surface, App = make(surface, block, surface.SurfaceSize) });
                }
                catch (Exception e)
                {
                    Log($"'{block.CustomName}': {appName} failed to start on screen {i + 1}: {e}");
                }
            }
            Schedule(t, seconds);
            Log($"'{block.CustomName}': {appName} on {t.Screens.Count} screen(s), {Describe(t)}");
        }

        // Sets the redraw interval and runs every screen straight away, so a changed tag shows at once.
        void Schedule(TaggedBlock t, int seconds)
        {
            t.Seconds = seconds;
            t.RedrawTicks = seconds > 0 ? seconds * 60 : t.Screens.Count > 0 ? OwnRate(t.Screens[0].App) : 100;
            foreach (var s in t.Screens) s.NextRunTick = _tick;
        }

        void Run(TaggedBlock t, Screen s)
        {
            // Same screen mode as S1: script mode with no script picked, so only our frames show.
            if (s.Surface.ContentType != ContentType.SCRIPT) s.Surface.ContentType = ContentType.SCRIPT;
            if (!string.IsNullOrEmpty(s.Surface.Script)) s.Surface.Script = "";

            Driving = true;
            _watch.Restart();
            try
            {
                s.App.Run();
            }
            catch (Exception e)
            {
                s.Failed = true;
                Log($"'{t.Block.CustomName}': {t.AppName} threw on screen {s.Index + 1} and was stopped: {e}");
            }
            finally
            {
                _watch.Stop();
                Driving = false;
            }

            double ms = _watch.Elapsed.TotalMilliseconds;
            _runs++;
            _totalMs += ms;
            if (ms > _maxMs) { _maxMs = ms; _maxWhere = $"{t.AppName} on '{t.Block.CustomName}'"; }
        }

        void Report()
        {
            if (_runs == 0) return;
            int screens = 0;
            foreach (var t in _tagged.Values) screens += t.Screens.Count;
            Log($"last minute: {_runs} runs on {screens} screen(s), average {_totalMs / _runs:0.00} ms, longest {_maxMs:0.00} ms ({_maxWhere}), total {_totalMs:0} ms");
            _runs = 0; _totalMs = 0; _maxMs = 0; _maxWhere = null;
        }

        // Disposes the block's apps. clearScreens blanks them too, so an untagged screen doesn't keep a stale frame.
        static void Drop(TaggedBlock t, bool clearScreens)
        {
            foreach (var s in t.Screens)
            {
                try
                {
                    s.App.Dispose();
                    if (clearScreens && !t.Block.Closed) s.Surface.ContentType = ContentType.NONE;
                }
                catch { }
            }
            t.Screens.Clear();
        }

        // The rate the game runs the app at from the Script menu.
        static int OwnRate(MyTextSurfaceScriptBase app)
        {
            return app.NeedsUpdate == ScriptUpdate.Update10 ? 10 : 100;
        }

        static string Describe(TaggedBlock t)
        {
            return t.Seconds > 0 ? $"every {t.Seconds} s" : $"every {t.RedrawTicks} ticks (the app's own rate)";
        }

        // "[ApexServer]", "[ApexServer:Cargo]", "[ApexServer:Cargo:5]" or "[ApexServer:5]".
        static bool ParseTag(string name, out string app, out int seconds)
        {
            app = DefaultApp;
            seconds = 0;
            int at = name.IndexOf(Tag, StringComparison.OrdinalIgnoreCase);
            if (at < 0) return false;
            int end = name.IndexOf(']', at);
            if (end < 0) end = name.Length;
            var parts = name.Substring(at + Tag.Length, end - at - Tag.Length).Split(':');
            for (int i = 1; i < parts.Length; i++)
            {
                var part = parts[i].Trim();
                int n;
                if (int.TryParse(part, out n)) seconds = Math.Max(1, Math.Min(MaxRedrawSeconds, n));
                else if (part.Length > 0) app = part;
            }
            return true;
        }

        static void Log(string message)
        {
            MyLog.Default.WriteLineAndConsole("[ApexServer S3] " + message);
        }
    }
}
