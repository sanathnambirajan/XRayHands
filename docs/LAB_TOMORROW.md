# Lab session, Mon 23.11.2026 (XVA Lab, ITZ 236)

## Bring
- Laptop with the project, charger
- USB-C cable (data, not charge-only)
- This file + `docs/DEVICE_TEST.md`

## 1. Set up the Quest 3 (5 min)
1. Developer mode on (ask the lab staff if the headset isn't already set up).
2. Settings → Movement tracking → Hand and body tracking: ON. Put the controllers away.
3. Plug the Quest into the laptop, put the headset on, allow USB debugging.

## 2. Install the app (2 min), in PowerShell on the laptop
```
cd C:\Users\sanat\XRayHands
& "C:\Program Files\Unity\Hub\Editor\6000.3.27f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe" devices
& "C:\Program Files\Unity\Hub\Editor\6000.3.27f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe" install -r Builds\XRayHands.apk
```
`devices` must list the headset. If it says "unauthorized", accept the prompt inside the headset.

## 3. Record the log (keep this running)
```
& "C:\Program Files\Unity\Hub\Editor\6000.3.27f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe" logcat -s Unity > xrayhands_log.txt
```
Start the app in the headset: App Library → Unknown sources → X-Ray Hands.

**If the app doesn't start or install:** use a lab PC, Quest Link on, open the project in Unity, press Play.

## 4. Test every gesture, 3 times each (10 min)
Mark ✅ works / ⚠️ awkward / ❌ fails, plus one sentence on what you saw.

| # | Do | Expect | Result |
|---|---|---|---|
| 1 | Look around | your room; metal block ~55 cm ahead, glowing pores | |
| 2 | Fist near the block, move hand | block follows, no jump | |
| 3 | Pinch with both hands, pull apart | block grows | |
| 4 | Flat palm into the block | near half gone, grey CT slice, "cut … mm" | |
| 5 | During 4, roll the wrist | contrast changes | |
| 6 | Two flat palms facing, spread/close | metal turns to glass, pores stay | |
| 7 | Point index finger into the block | value label, tone, faster clicks near pores | |
| 8 | Pinch a pore, pull ~12 cm | pores fly into a 3D chart | |
| 9 | Pinch a chart dot | dot white, its pore glows, line + tooltip | |
| 10 | Both hands pinching in the chart, spread | box, "N selected" | |
| 11 | Thumb-index "O" in front of your eye | block peeled inside the ring | |
| 12 | Palm up, close to fist, hold ~1 s | everything resets | |

Also note: gestures that fire by accident, frame drops (and what was active), labels too small/big.

## 5. Take home
- `xrayhands_log.txt`
- the filled table above
- 2–3 headset screenshots (Meta button → camera)

## 6. Ask the professor
1. Is our progress OK for this point (M1), with device testing starting now?
2. NIST sample + Enigma scan OK, or would he prefer one of his institute's datasets?
3. Is the 3D scatter plot the right chart for pores?
4. Evaluation: small user study vs a 2D slice viewer, or demo only?
5. Is precision 0.90 / recall 0.75 good enough, or improve the segmentation?
6. Can we use or borrow a Quest outside Mondays?
7. What exactly should the 1-minute progress video (due 14.12, 10:00) show?
8. Confirm deadlines: implementation 25.01.2027 17:00, demo 01.02.2027.

---

## Next lab visit: make the gestures work with real hands (Quest Link / Air Link)

Status: real hands are tracked over Air Link (`[XRayHands] Hands: XR hand tracking`), but
gestures did not fire yet. The app now logs what it measures from the hands every 0.5 s
(`[XRayHands][diag]` lines: finger curls, pinch distance, distance to the block, every
gesture's confidence, the running tool). Claude reads these from the Unity log and tunes the
thresholds.

1. Laptop: open the project in Unity (`Main.unity`). Meta Horizon Link app running.
2. Headset: hand tracking ON, controllers put away. Start Air Link (or Link with a data cable).
3. Tell Claude "connected". Claude presses Play and checks the hands line.
4. If the block is not right in front of you: press **Backspace** on the laptop (re-centres it).
5. Hold each pose for ~5 s, saying which one you are doing:
   - open flat hand, fingers together, palm into the block (cut)
   - two flat hands facing each other beside the block (peel)
   - index finger pointing into the block (probe)
   - thumb and index pinched (pinch)
   - fist near the block (grab)
   - thumb + index "O" in front of your eye (lens)
6. Claude compares the logged numbers with the thresholds and adjusts `GestureTuning`.
