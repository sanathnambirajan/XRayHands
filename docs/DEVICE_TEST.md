# Device test script (XVA Lab, Quest 3), about 10 minutes

Goal: confirm every gesture works on real hands and collect what we need to tune it.
Report each step as ✅ / ⚠️ (works but awkward) / ❌, plus one sentence on what you saw.

## Before you start (2 min)
1. Quest 3 in **developer mode**, USB-C cable to the lab PC, allow USB debugging in the headset.
2. Hand tracking ON (Settings → Movement tracking → Hand and body tracking), controllers put away.
3. Install: `adb install -r Builds/XRayHands.apk` (or Unity → Build Profiles → Android → Build And Run).
   Fallback (PCVR): Quest Link on, Unity Play mode with the Windows build target.
4. Start logging in a terminal on the PC: `adb logcat -s Unity > xrayhands_log.txt`

## Gestures (6 min); do each 3 times
| # | Do this | You should see / hear |
|---|---|---|
| 1 | Look around | room passthrough; the NIST specimen ~55 cm ahead at chest height, steel-grey with glowing pores |
| 2 | Fist near the specimen, move and rotate the hand | specimen follows rigidly, no jump at grab start |
| 3 | Pinch with both hands near the specimen, pull apart | specimen grows about the point between your hands; label "×…" between hands |
| 4 | Flat palm, slide it into the specimen | near half disappears; cut face shows the grey CT slice; ring + "cut … mm" label; release keeps the cut |
| 5 | During 4, roll the wrist | slice contrast changes smoothly |
| 6 | Two flat palms facing each other beside the specimen, spread/close | metal fades to glass, pores stay; dashed line + "Peel …%" |
| 7 | Point with the index finger into the specimen | label with material + grey value; tone from your fingertip, pitch follows density; clicks speed up near pores; ring pulses at fingertip |
| 8 | Pinch a glowing pore, pull ~12 cm away | all pores fly out on arcs into a 3D chart to the right |
| 9 | Pinch a dot in the chart | dot turns white, its pore glows, dashed link line + tooltip |
| 10 | Both hands pinching inside the chart, spread | box brush, "N selected", pores highlighted in the specimen |
| 11 | Thumb-index "O" in front of your eye, look through it at the specimen | inside the ring the specimen is peeled |
| 12 | Palm up, close to a fist, hold ~1 s | everything resets, confirmation ripple |

## What to report (2 min)
- Any gesture that triggered by accident, or that you had to try more than twice.
- Frame drops: when, and what was active (peel? lens? chart?).
- Labels too small / too large / clipped.
- Send back `xrayhands_log.txt` and 2–3 screenshots (Quest: Meta button → camera).

When a bug is reported, we first reproduce it with a scripted gesture timeline in the editor, then fix.
