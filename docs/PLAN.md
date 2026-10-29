# X-Ray Hands: plan and status

Status legend: âœ… done and verified Â· ðŸŸ¡ implemented, not yet verified end-to-end Â· â¬œ not started Â· ðŸ”’ needs the lab / the students

## Phase 0: environment
| Item | Status | Notes |
|---|---|---|
| Unity 6000.3.27f1 | âœ… | brief says 25f1; same 6.3 LTS line, already installed (saves ~5 GB) |
| Android Build Support | âœ… | installed headless via Unity Hub CLI after freeing disk (OpenJDK 17, SDK platforms 34â€“37, NDK r27c) |
| Git, Python 3.11 venv, uv, Node 24 LTS | âœ… | |
| Project `C:/Users/sanat/XRayHands`, URP 3D template | âœ… | |
| git + Unity .gitignore + LFS .gitattributes | âœ… | no remote yet (ask before creating) |
| MCP for Unity | âœ… | console, hierarchy, tests, screenshots all driven through MCP |
| Meta agentic tools: meta-vr plugin 2.0.0 | âœ… | marketplace is named `meta-vr` (install id `meta-vr@meta-vr`); skills load after a Claude Code restart |
| metavr MCP | âœ… | provided by the meta-vr plugin itself (tools `mcp__plugin_meta-vr_metavr__*`), no separate npx install needed |
| Quest APK | âœ… | `XRayHands â†’ Build Quest APK` or batch mode: `Unity.exe -batchmode -quit -projectPath . -executeMethod XRayHands.EditorTools.BuildScript.BuildQuest`; 89.7 MB, 0 errors; not yet run on a headset |
| OpenXR, Meta OpenXR 2.7.0, XR Hands 1.10.0, AR Foundation 6.6.0 | âœ… | versions resolved by the Unity package manager for 6000.3 |
| OpenXR loader + Meta Quest feature + hand tracking + passthrough | âœ… | `XRayHands â†’ Configure XR + Player (Quest)` (Editor/ProjectSetup.cs), reproducible on lab PCs; OpenXR validation: 0 errors, 1 benign warning (SSAO disabled but not removed) |
| Player settings (ARM64, IL2CPP, Vulkan, SPI, linear, API 32, MSAA 4Ã—) | âœ… | 72 Hz is the Quest 3 default; fixed foveation still â¬œ |
| XR Origin + AR Session (passthrough) | âœ… | enabled only when an XR runtime is running; desktop mode has 0 warnings |
| GitHub remote | ðŸ”’ | `gh` installed; needs `gh auth login` by Sanath, then private repo `sanathnambirajan/XRayHands` |
| Meta XR Simulator | â¬œ | `SimulatedHandSource` covers headset-free testing meanwhile |

## M1 (target 23.11.2026)
| Task | Acceptance | Status |
|---|---|---|
| Python pipeline + tests | 28 pytest green; both specimens validate | âœ… |
| NIST sample selection | ranking from reference data | âœ… sample 2 |
| Volume rendering | stereo-safe URP ray marcher, ESS, jitter, opacity correction | âœ… editor; stereo check â¬œ |
| Grab / scale / cut / peel | gesture â†’ tool â†’ visible effect | ðŸŸ¡ tools done; rendering effects verified via API; gesture timelines â¬œ |
| Device test script | docs/DEVICE_TEST.md | âœ… written |

## M2 (14.12.2026)
| Task | Status |
|---|---|
| Probe + spatial sonification + ring pulse | ðŸŸ¡ |
| Extract animation + linked 3D chart + link line + tooltip | âœ… verified in editor (screenshot) |
| Chart dot selection, two-hand brushing | ðŸŸ¡ |
| RecordingRig for the 1-min progress video | â¬œ |

## M3 (11.01.2027)
| Task | Status |
|---|---|
| Lens (Tier 2) | ðŸŸ¡ shader + tool done |
| Contrast dial (Tier 2) | ðŸŸ¡ |
| Reset (Tier 2) | ðŸŸ¡ |
| Ground-truth TP/FP/FN view + metrics label | âœ… verified in editor |
| Specimen shelf (grab miniatures) | â¬œ (keys 1/2 load specimens meanwhile) |
| Slice cards | â¬œ |
| Quality tiers auto-select by frame time | â¬œ (manual Tab works) |

## M4 / M5
PlayMode gesture-timeline tests, 12-angle screenshot harness, PERFORMANCE.md on device,
code-review pass, submission script `tools/make_submission.ps1`, slides + demo script. â¬œ
