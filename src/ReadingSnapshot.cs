using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace MhiagosControl
{
    internal enum ReadingState { Waiting, Live, Failed, Expired, Paused }

    internal interface IReadingStatusData
    {
        ReadingState CurrentReadingState(string sensorId);
    }

    /// <summary>Instantaneo imutavel com validade independente para hardware e RTSS.</summary>
    internal sealed class ReadingSnapshot
    {
        private readonly Dictionary<string, float> _hardware, _fast;
        private readonly long? _hardwareAt, _fastAt;
        private readonly bool _failed;
        internal static readonly ReadingSnapshot Empty = new ReadingSnapshot(null, null, null, null, false);
        internal static readonly ReadingSnapshot Failed = new ReadingSnapshot(null, null, null, null, true);

        private ReadingSnapshot(Dictionary<string, float> hardware, Dictionary<string, float> fast,
            long? hardwareAt, long? fastAt, bool failed)
        {
            _hardware = hardware ?? new Dictionary<string, float>();
            _fast = fast ?? new Dictionary<string, float>();
            _hardwareAt = hardwareAt; _fastAt = fastAt; _failed = failed;
        }

        private static bool IsFast(string id)
        { return id != null && id.StartsWith(Rtss.Prefixo, StringComparison.OrdinalIgnoreCase); }

        private static Dictionary<string, float> Filter(IDictionary<string, float> source, bool fast)
        {
            Dictionary<string, float> result = new Dictionary<string, float>();
            if (source != null)
                foreach (KeyValuePair<string, float> pair in source)
                    if (IsFast(pair.Key) == fast && !float.IsNaN(pair.Value) && !float.IsInfinity(pair.Value))
                        result[pair.Key] = pair.Value;
            return result;
        }

        internal static ReadingSnapshot Full(IDictionary<string, float> values, long now)
        { return new ReadingSnapshot(Filter(values, false), Filter(values, true), now, now, false); }

        internal ReadingSnapshot WithFast(IDictionary<string, float> values, long now)
        { return new ReadingSnapshot(_hardware, Filter(values, true), _hardwareAt, now, _failed); }

        private static bool Valid(long? sampledAt, long now)
        {
            return sampledAt.HasValue && now >= sampledAt.Value &&
                now - sampledAt.Value <= (long)PanelKeepalive.MaxReadingAgeMs * Stopwatch.Frequency / 1000;
        }

        internal Dictionary<string, float> Read(long now, bool paused)
        {
            Dictionary<string, float> result = new Dictionary<string, float>();
            if (paused || _failed) return result;
            if (Valid(_hardwareAt, now)) foreach (KeyValuePair<string, float> p in _hardware) result[p.Key] = p.Value;
            if (Valid(_fastAt, now)) foreach (KeyValuePair<string, float> p in _fast) result[p.Key] = p.Value;
            return result;
        }

        internal ReadingState State(string id, long now, bool paused)
        {
            if (paused) return ReadingState.Paused;
            if (_failed) return ReadingState.Failed;
            if (id != null)
            {
                bool fast = IsFast(id);
                if (!(fast ? _fast : _hardware).ContainsKey(id)) return ReadingState.Waiting;
                return Valid(fast ? _fastAt : _hardwareAt, now) ? ReadingState.Live : ReadingState.Expired;
            }
            if ((_hardware.Count > 0 && !Valid(_hardwareAt, now)) || (_fast.Count > 0 && !Valid(_fastAt, now)))
                return ReadingState.Expired;
            return _hardware.Count + _fast.Count > 0 ? ReadingState.Live : ReadingState.Waiting;
        }
    }
}
