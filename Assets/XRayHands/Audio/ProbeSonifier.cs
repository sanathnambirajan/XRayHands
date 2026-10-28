using System;
using UnityEngine;
using XRayHands.Interaction;

namespace XRayHands.Audio
{
    /// <summary>
    /// Spatial sonification of the probe (multisensory analytics):
    /// <list type="bullet">
    /// <item>a continuous tone whose pitch rises with density (two octaves, 220–880 Hz);</item>
    /// <item>Geiger-style clicks whose rate rises as the nearest defect gets closer.</item>
    /// </list>
    /// The sound is synthesised sample by sample in <see cref="OnAudioFilterRead"/> on an
    /// AudioSource that sits on the fingertip, so Unity's 3D panning makes it come from the
    /// finger. Every click is also reported back (<see cref="Clicked"/>) so the app can draw a
    /// ring pulse: the information stays readable without sound.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class ProbeSonifier : MonoBehaviour
    {
        /// <summary>Tone frequency at density 0, Hz.</summary>
        public const float BaseFrequencyHz = 220f;
        /// <summary>Pitch range in octaves across the density range.</summary>
        public const float OctaveRange = 2f;
        /// <summary>Click rate far from any defect, Hz.</summary>
        public const float MinClickRateHz = 1.5f;
        /// <summary>Click rate at a defect surface, Hz.</summary>
        public const float MaxClickRateHz = 40f;
        /// <summary>Distance (fraction of specimen size) over which the click rate falls by e.</summary>
        public const float ClickFalloffFraction = 0.03f;

        private const float ToneGain = 0.06f;
        private const float ClickGain = 0.5f;
        private const float ClickSeconds = 0.004f;
        private const float GlideSeconds = 0.05f; // smooth pitch changes, no zipper noise

        // Written on the main thread, read on the audio thread. Single floats are atomic on
        // all our targets; a torn read would only cause one slightly wrong sample.
        private volatile bool _active;
        private float _targetFrequency = BaseFrequencyHz;
        private float _clickRate = MinClickRateHz;
        private int _pendingClicks;

        private float _frequency = BaseFrequencyHz;
        private double _phase;
        private double _clickClock;
        private float _clickRemaining;
        private int _sampleRate;
        private uint _noise = 22222;

        /// <summary>Raised on the main thread once per click (for the visual ring pulse).</summary>
        public event Action<Vector3> Clicked;

        /// <summary>Current click rate (tests).</summary>
        public float ClickRateHz => _clickRate;

        private void Awake()
        {
            _sampleRate = AudioSettings.outputSampleRate;
            AudioSource src = GetComponent<AudioSource>();
            src.spatialBlend = 1f;
            src.loop = true;
            src.playOnAwake = true;
            src.minDistance = 0.3f;
            // OnAudioFilterRead only runs on a playing source; a silent looping clip keeps it alive.
            src.clip = AudioClip.Create("Silence", _sampleRate, 1, _sampleRate, false);
            src.Play();
        }

        /// <summary>Feeds this frame's probe reading. Specimen size normalises the click distance.</summary>
        public void UpdateReading(in ProbeReading r, float specimenSizeMm)
        {
            _active = r.Inside;
            if (!r.Inside) return;
            transform.position = r.WorldPosition;
            _targetFrequency = BaseFrequencyHz * Mathf.Pow(2f, OctaveRange * r.Density01);
            _clickRate = ClickRate(r.NearestDefectMm, specimenSizeMm);
        }

        /// <summary>Click rate for a distance; exponential so rate changes are felt evenly near and far.</summary>
        public static float ClickRate(float nearestDefectMm, float specimenSizeMm)
        {
            float d = nearestDefectMm / Mathf.Max(specimenSizeMm, 1e-6f);
            return Mathf.Lerp(MinClickRateHz, MaxClickRateHz, Mathf.Exp(-d / ClickFalloffFraction));
        }

        private void Update()
        {
            int clicks = System.Threading.Interlocked.Exchange(ref _pendingClicks, 0);
            for (int i = 0; i < clicks && i < 4; i++) Clicked?.Invoke(transform.position);
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (!_active) return;
            double dt = 1.0 / _sampleRate;
            float glide = (float)(dt / GlideSeconds);
            for (int i = 0; i < data.Length; i += channels)
            {
                _frequency += (_targetFrequency - _frequency) * glide;
                _phase += 2.0 * Math.PI * _frequency * dt;
                if (_phase > 2.0 * Math.PI) _phase -= 2.0 * Math.PI;
                float s = ToneGain * (float)Math.Sin(_phase);

                _clickClock += dt * _clickRate;
                if (_clickClock >= 1.0)
                {
                    _clickClock -= 1.0;
                    _clickRemaining = ClickSeconds;
                    System.Threading.Interlocked.Increment(ref _pendingClicks);
                }
                if (_clickRemaining > 0f)
                {
                    _clickRemaining -= (float)dt;
                    _noise ^= _noise << 13; _noise ^= _noise >> 17; _noise ^= _noise << 5; // xorshift white noise
                    s += ClickGain * ((_noise & 0xFFFF) / 32768f - 1f);
                }
                for (int c = 0; c < channels; c++) data[i + c] += s;
            }
        }
    }
}
