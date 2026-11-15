using UnityEngine;

namespace LucidCatsFlashlight;

/// <summary>
/// Procedural static-crackle clip for dying flashlights: a soft white-noise
/// hiss with random crackle pops, generated entirely in code (no assets).
/// One clip is created lazily and shared by every rig.
/// </summary>
public static class FlashlightStatic
{
    private static AudioClip _clip;

    public static AudioClip GetClip()
    {
        if (_clip == null)
        {
            _clip = Create();
        }

        return _clip;
    }

    private static AudioClip Create()
    {
        try
        {
            int sampleRate = 22050;      // plenty for noise; keeps the buffer small
            float seconds = 1.6f;        // loop length; pops wrap the seam cleanly
            int samples = Mathf.RoundToInt(sampleRate * seconds);
            var data = new float[samples];
            var rng = new System.Random(1234); // deterministic: same texture every time

            // Base layer: quiet white-noise hiss.
            const float hiss = 0.10f;
            for (int i = 0; i < samples; i++)
            {
                data[i] = (float)(rng.NextDouble() * 2.0 - 1.0) * hiss;
            }

            // Crackle layer: random noise bursts with a fast exponential decay.
            for (int p = 0; p < 26; p++)
            {
                int start = rng.Next(samples);
                int length = 120 + rng.Next(400); // ~5-23 ms at 22 kHz
                float amp = 0.35f + (float)rng.NextDouble() * 0.45f;
                for (int i = 0; i < length; i++)
                {
                    int idx = (start + i) % samples; // wraps the loop seam invisibly
                    float decay = Mathf.Exp(-i / (length * 0.25f));
                    data[idx] += (float)(rng.NextDouble() * 2.0 - 1.0) * amp * decay;
                }
            }

            // Cross-fade the loop seam so no pop on each wrap.
            const int fade = 240; // ~11 ms
            for (int i = 0; i < fade; i++)
            {
                float t = i / (float)fade;
                data[i] *= t;
                data[samples - 1 - i] *= t;
            }

            var clip = AudioClip.Create("LCF_Static", samples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
        catch (System.Exception e)
        {
            FlashlightPlugin.Log.LogWarning($"Static clip could not be generated ({e.Message}).");
            return null;
        }
    }
}
