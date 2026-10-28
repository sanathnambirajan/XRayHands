# X-Ray Hands: plan and status

Status legend: ✅ done and verified · 🟡 implemented, not yet verified end-to-end · ⬜ not started · 🔒 needs the lab / the students

## Phase 0: environment
| Item | Status | Notes |
|---|---|---|
| Unity 6000.3.27f1 | ✅ | brief says 25f1; same 6.3 LTS line, already installed (saves ~5 GB) |
| Android Build Support | 🔒 | headless Hub install failed ("error while executing the installer", disk was at 2 GB); install via Unity Hub GUI → Installs → 6000.3.27f1 → Add modules → Android Build Support (+ OpenJDK, SDK & NDK). Needs ~6 GB free |
| Git, Python 3.11 venv, uv, Node 24 LTS | ✅ | |
| Project `C:/Users/sanat/XRayHands`, URP 3D template | ✅ | |
| git + Unity .gitignore + LFS .gitattributes | ✅ | no remote yet (ask before creating) |
| MCP for Unity | ✅ | console, hierarchy, tests, screenshots all driven through MCP |
| Meta agentic tools (plugin + metavr MCP) | ⬜ | Node is installed; `/plugin` commands must be run by a user in Claude Code |
| OpenXR, Meta OpenXR 2.7.0, XR Hands 1.10.0, AR Foundation 6.6.0 | ✅ | versions resolved by the Unity package manager for 6000.3 |
| OpenXR loader + Meta Quest feature + hand tracking enabled | ⬜ | next step; Android tab needs the Android module |
| Player settings (ARM64, IL2CPP, Vulkan, SPI, linear, 72 Hz) | ⬜ | after the Android module |
| Meta XR Simulator | ⬜ | `SimulatedHandSource` covers headset-free testing meanwhile |

## M1 (target 23.11.2026)
| Task | Acceptance | Status |
|---|---|---|
| Python pipeline + tests | 28 pytest green; both specimens validate | ✅ |
| NIST sample selection | ranking from reference data | ✅ sample 2 |
| Volume rendering | stereo-safe URP ray marcher, ESS, jitter, opacity correction | ✅ editor; stereo check ⬜ |
| Grab / scale / cut / peel | gesture → tool → visible effect | 🟡 tools done; rendering effects verified via API; gesture timelines ⬜ |
| Device test script | docs/DEVICE_TEST.md | ⬜ |

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
