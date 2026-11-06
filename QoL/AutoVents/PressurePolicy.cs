using System;

namespace Ostrabus.QoL.AutoVents;

// Scalar history only. An observation gap does not prove sustained low pressure.
internal struct PressurePolicy {
    double lastEpoch, lowSince;
    string room;
    bool initialized, low;
    internal double Oxygen { get; private set; }
    internal double LowSeconds => low ? Math.Max(0, lastEpoch - lowSince) : 0;
    internal bool Observe(double oxygen, bool invalidRoom, string roomId, double epoch, double cutoff, double emergency, double grace) {
        bool discontinuity = !initialized || room != roomId || epoch < lastEpoch || epoch - lastEpoch > 2.5;
        initialized = true; room = roomId; lastEpoch = epoch;
        Oxygen = oxygen;
        if (invalidRoom || double.IsNaN(oxygen) || double.IsInfinity(oxygen) || oxygen < 0 || oxygen <= emergency) { low = false; return true; }
        if (oxygen >= cutoff) { low = false; return false; }
        if (!low || discontinuity) { lowSince = epoch; low = true; }
        return epoch - lowSince >= grace;
    }
}
