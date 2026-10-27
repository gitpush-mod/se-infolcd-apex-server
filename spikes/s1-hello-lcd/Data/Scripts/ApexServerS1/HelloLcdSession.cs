// S1 spike (se-infolcd-apex-server#4). Throwaway: lives on a spike branch, never merged as-is.
//
// A server-only session component that draws a test pattern on every screen of any block whose
// name contains "[ApexServer]". "[ApexServer:N]" redraws every N seconds (default 2, max 600).
// It answers one question: does a frame drawn on the server reach the players who look at it?
//
// Each frame shows a draw counter and the server's clock, so the checks can be read off the screen:
//   - late join: a player joining between draws should see the last frame, not a blank screen
//   - save/load: right after a reload the old frame should still show until the next draw
//   - multi-screen blocks: every screen shows "Screen i of n"

using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;
using IngameSurface = Sandbox.ModAPI.Ingame.IMyTextSurface;
using IngameSurfaceProvider = Sandbox.ModAPI.Ingame.IMyTextSurfaceProvider;

namespace InfoLcdApexServer.Spikes
{
    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public class HelloLcdSession : MySessionComponentBase
    {
        const string Tag = "[ApexServer";
        const int ScanEveryTicks = 300;          // look for tagged blocks every 5 s
        const int DefaultRedrawSeconds = 2;
        const int MaxRedrawSeconds = 600;

        static readonly Color Background = new Color(10, 16, 33);
        static readonly Color Accent = new Color(37, 99, 235);
        static readonly Color[] Bars =
            { Color.Red, Color.Orange, Color.Yellow, Color.Lime, Color.Cyan, Color.Blue, Color.Magenta, Color.White };

        class Target
        {
            public IMyTerminalBlock Block;
            public int RedrawTicks;
            public int NextDrawTick;
            public int Draws;
        }

        readonly HashSet<IMyEntity> _grids = new HashSet<IMyEntity>();
        readonly List<IMySlimBlock> _slims = new List<IMySlimBlock>();
        readonly List<Target> _targets = new List<Target>();
        readonly Dictionary<long, Target> _known = new Dictionary<long, Target>();
        int _tick;
        bool _started, _isServer;

        public override void UpdateAfterSimulation()
        {
            if (!_started)
            {
                _started = true;
                // Published builds carry the Server Scripts tag, so clients never even load this.
                // Local copies have no tag and load everywhere: this gate keeps clients idle.
                _isServer = MyAPIGateway.Multiplayer.IsServer;
                Log(_isServer ? "server: drawing screens on blocks tagged [ApexServer]" : "client: idle (IsServer=false)");
            }
            if (!_isServer) return;

            _tick++;
            if (_tick % ScanEveryTicks == 1) Scan();

            foreach (var t in _targets)
            {
                if (_tick < t.NextDrawTick) continue;
                Draw(t);
                t.NextDrawTick = _tick + t.RedrawTicks;
            }
        }

        protected override void UnloadData()
        {
            _grids.Clear();
            _slims.Clear();
            _targets.Clear();
            _known.Clear();
        }

        void Scan()
        {
            int before = _targets.Count;
            _targets.Clear();
            _grids.Clear();
            MyAPIGateway.Entities.GetEntities(_grids, e => e is IMyCubeGrid);
            foreach (var entity in _grids)
            {
                _slims.Clear();
                ((IMyCubeGrid)entity).GetBlocks(_slims, s => s.FatBlock is IngameSurfaceProvider && s.FatBlock is IMyTerminalBlock);
                foreach (var slim in _slims)
                {
                    var block = (IMyTerminalBlock)slim.FatBlock;
                    string name = block.CustomName ?? "";
                    int at = name.IndexOf(Tag, StringComparison.OrdinalIgnoreCase);
                    if (at < 0) continue;

                    Target t;
                    if (!_known.TryGetValue(block.EntityId, out t))
                    {
                        // First draw waits one interval, so a reloaded or late-joined screen shows
                        // whatever frame it already had for a while before we replace it.
                        t = new Target { Block = block };
                        t.RedrawTicks = ParseSeconds(name, at) * 60;
                        t.NextDrawTick = _tick + t.RedrawTicks;
                        _known[block.EntityId] = t;
                    }
                    t.RedrawTicks = ParseSeconds(name, at) * 60;
                    _targets.Add(t);
                }
            }
            if (_targets.Count != before) Log("tagged blocks: " + _targets.Count);
        }

        static int ParseSeconds(string name, int at)
        {
            int i = at + Tag.Length;
            if (i < name.Length && name[i] == ':')
            {
                int end = name.IndexOf(']', i);
                int seconds;
                if (end > i + 1 && int.TryParse(name.Substring(i + 1, end - i - 1), out seconds))
                    return Math.Max(1, Math.Min(MaxRedrawSeconds, seconds));
            }
            return DefaultRedrawSeconds;
        }

        void Draw(Target t)
        {
            if (t.Block == null || t.Block.Closed) return;
            var provider = (IngameSurfaceProvider)t.Block;
            t.Draws++;
            for (int i = 0; i < provider.SurfaceCount; i++)
            {
                var surface = provider.GetSurface(i);
                if (surface == null) continue;
                // Drawn sprites only show in script mode with no script selected, like a PB screen.
                if (surface.ContentType != ContentType.SCRIPT) surface.ContentType = ContentType.SCRIPT;
                if (!string.IsNullOrEmpty(surface.Script)) surface.Script = "";
                DrawPattern(surface, i, provider.SurfaceCount, t);
            }
            if (t.Draws == 1) Log("first frame on '" + t.Block.CustomName + "' (" + provider.SurfaceCount + " screen(s))");
        }

        static void DrawPattern(IngameSurface surface, int index, int count, Target t)
        {
            Vector2 size = surface.SurfaceSize;
            Vector2 origin = (surface.TextureSize - size) / 2f;
            Vector2 center = origin + size / 2f;
            float unit = Math.Min(size.X, size.Y) / 512f;   // 1.0 on a standard 512 px square LCD

            var frame = surface.DrawFrame();
            frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple", center, size, Background));

            // Border: proves the frame covers the whole screen area.
            float edge = Math.Max(2f, 6f * unit);
            frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple", new Vector2(center.X, origin.Y + edge / 2f), new Vector2(size.X, edge), Accent));
            frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple", new Vector2(center.X, origin.Y + size.Y - edge / 2f), new Vector2(size.X, edge), Accent));
            frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple", new Vector2(origin.X + edge / 2f, center.Y), new Vector2(edge, size.Y), Accent));
            frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple", new Vector2(origin.X + size.X - edge / 2f, center.Y), new Vector2(edge, size.Y), Accent));

            // Colour bars: proves colours and positions survive the trip.
            float barWidth = (size.X - 2 * edge) / Bars.Length;
            float barHeight = size.Y * 0.18f;
            for (int b = 0; b < Bars.Length; b++)
                frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple",
                    new Vector2(origin.X + edge + barWidth * (b + 0.5f), origin.Y + size.Y - edge - barHeight / 2f),
                    new Vector2(barWidth, barHeight), Bars[b]));

            string[] lines =
            {
                "INFOLCD APEX SERVER",
                "S1 test pattern",
                "Draw #" + t.Draws,
                "Server " + DateTime.UtcNow.ToString("HH:mm:ss") + " UTC",
                "Screen " + (index + 1) + " of " + count,
                "Every " + (t.RedrawTicks / 60) + " s",
            };
            float scale = 1.1f * unit;
            float lineHeight = 34f * scale;
            float y = origin.Y + edge + size.Y * 0.05f;
            for (int l = 0; l < lines.Length; l++)
            {
                var text = MySprite.CreateText(lines[l], "White", l == 0 ? Accent : Color.White, l == 0 ? scale * 1.2f : scale, TextAlignment.CENTER);
                text.Position = new Vector2(center.X, y);
                frame.Add(text);
                y += l == 0 ? lineHeight * 1.3f : lineHeight;
            }
            frame.Dispose();
        }

        static void Log(string message)
        {
            MyLog.Default.WriteLineAndConsole("[ApexServer S1] " + message);
        }
    }
}
