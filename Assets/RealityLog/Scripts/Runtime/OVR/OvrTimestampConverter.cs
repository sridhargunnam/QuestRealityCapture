# nullable enable

using System;

namespace RealityLog.OVR
{
    class OvrTimestampConverter
    {
        private double baseOvrTimeSec;
        private long baseUnixTimeMs;

        public void Reset()
        {
            baseOvrTimeSec = OVRPlugin.GetTimeInSeconds();
            baseUnixTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        public long ConvertOvrSecToUnixTimeMs(double ovrTime)
        {
            var deltaSec = ovrTime - baseOvrTimeSec;
            var deltaMs = (long)(deltaSec * 1000.0);
            return baseUnixTimeMs + deltaMs;
        }
    }
}
