# Undocked six-hour SFF result

Start 65631786485.993408; skip 21600 seconds; end 65631808085.993408.
Player was and remains native orbit-locked; no NavData or thrust.
Actual separation from VORB 4391.900732097 km; predicted 4391.900732096 km.
Player positional residual against captured native orbit plus fixed BO offset:
0.0002394 metres. The fixed offset is essential to reproduce native TimeAdvance.
RCS fuel unchanged at 5993.42829197538 kg.

66 hidden exclusions; 7 plans: 3 coasts, 2 partial journeys and 2 arrivals.
Zero failures, impact checks or budget fallbacks. SFF context 409.6638 ms.
Recorded arrivals: O-P3JU and O-B7R to M-5N4; native arrival allowed.
Recorded partials: O-TRRP toward VORB and J-V5P/O-X6P.

This passes the intended undocked native-coasting preservation case. It does not
exercise custom numerical SFF translation on the player, because native orbit
lock deliberately excludes it, nor does it validate active plotted torch playback.
The six-hour station separation is expected orbital phase drift, not a new escape.
Recorder stopped. All raw baseline/end/prediction/comparison artifacts retained.
