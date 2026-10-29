# X-Ray Hands: plan and status

Status legend: ✅ done and verified · 🟡 implemented, not yet verified end-to-end · ⬜ not started · 🔒 needs the lab / the students

## Phase 0: environment
| Item | Status | Notes |
|---|---|---|
| Unity 6000.3.27f1 | ✅ | brief says 25f1; same 6.3 LTS line, already installed (saves ~5 GB) |
| Android Build Support | ✅ | installed headless via Unity Hub CLI after freeing disk (OpenJDK 17, SDK platforms 34–37, NDK r27c) |
| Git, Python 3.11 venv, uv, Node 24 LTS | ✅ | |
| Project `C:/Users/sanat/XRayHands`, URP 3D template | ✅ | |
| git + Unity .gitignore + LFS .gitattributes | ✅ | remote: GitHub (private) |
| MCP for Unity | ✅ | console, hierarchy, tests, screenshots all driven through MCP |
| Meta agentic tools: meta-vr plugin 2.0.0 | ✅ | marketplace is named `meta-vr` (install id `meta-vr@meta-vr`); skills load after a Claude Code restart |
| metavr MCP | ✅ | provided by the meta-vr plugin itself (tools `mcp__plugin_meta-vr_metavr__*`), no separate npx install needed |
| Quest APK | ✅ | `XRayHands → Build Quest APK` or batch mode: `Unity.exe -batchmode -quit -projectPath . -executeMethod XRayHands.EditorTools.BuildScript.BuildQuest`; 89.7 MB, 0 errors; not yet run on a headset |
| OpenXR, Meta OpenXR 2.7.0, XR Hands 1.10.0, AR Foundation 6.6.0 | ✅ | versions resolved by the Unity package manager for 6000.3 |
| OpenXR loader + Meta Quest feature + hand tracking + passthrough | ✅ | `XRayHands → Configure XR + Player (Quest)` (Editor/ProjectSetup.cs), reproducible on lab PCs; OpenXR validation: 0 errors, 1 benign warning (SSAO disabled but not removed) |
| Player settings (ARM64, IL2CPP, Vulkan, SPI, linear, API 32, MSAA 4×) | ✅ | 72 Hz is the Quest 3 default; fixed foveation still ⬜ |
| XR Origin + AR Session (passthrough) | ✅ | enabled only when an XR runtime is running; desktop mode has 0 warnings |
| GitHub remote | ✅ | private repo https://github.com/sanathnambirajan/XRayHands (main, LFS objects uploaded) |
| Meta XR Simulator | ⬜ | `SimulatedHandSource` covers headset-free testing meanwhile |

## M1 (target 23.11.2026)
| Task | Acceptance | Status |
|---|---|---|
| Python pipeline + tests | 28 pytest green; both specimens validate | ✅ |
| NIST sample selection | ranking from reference data | ✅ sample 2 |
| Volume rendering | stereo-safe URP ray marcher, ESS, jitter, opacity correction | ✅ editor; stereo check ⬜ |
| Grab / scale / cut / peel | gesture → tool → visible effect | 🟡 tools done; rendering effects verified via API; gesture timelines ⬜ |
| Device test script | docs/DEVICE_TEST.md | ✅ written |

## M2 (14.12.2026)
| Task | Status |
|---|---|
| Probe + spatial sonification + ring pulse | 🟡 |
| Extract animation + linked 3D chart + link line + tooltip | ✅ verified in editor (screenshot) |
| Chart dot selection, two-hand brushing | 🟡 |
| RecordingRig for the 1-min progress video | ⬜ |

## M3 (11.01.2027)
| Task | Status |
|---|---|
| Lens (Tier 2) | 🟡 shader + tool done |
| Contrast dial (Tier 2) | 🟡 |
| Reset (Tier 2) | 🟡 |
| Ground-truth TP/FP/FN view + metrics label | ✅ verified in editor |
| Specimen shelf (grab miniatures) | ⬜ (keys 1/2 load specimens meanwhile) |
| Slice cards | ⬜ |
| Quality tiers auto-select by frame time | ⬜ (manual Tab works) |

## M4 / M5
PlayMode gesture-timeline tests, 12-angle screenshot harness, PERFORMANCE.md on device,
code-review pass, submission script `tools/make_submission.ps1`, slides + demo script. ⬜
