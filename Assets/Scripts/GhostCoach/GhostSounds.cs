using System.Collections.Generic;
using UnityEngine;

// Short sounds for the menu, made in code (no audio files): a soft click when
// a button is pressed and a two-tone chime when a stroke is chosen.  Not
// feedback on the player's stroke, so they do not mix with the experiment's
// variants.
public static class GhostSounds {

    static Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
    const int rate = 44100;

    static AudioClip make(string name, float[] hertz, float seconds_each) {
        int each = Mathf.RoundToInt(seconds_each * rate);
        float[] data = new float[each * hertz.Length];
        for (int n = 0 ; n < hertz.Length ; ++n)
            for (int i = 0 ; i < each ; ++i) {
                float t = (float)i / rate;
                float attack = Mathf.Clamp01(i / (0.003f * rate));
                float decay = Mathf.Exp(-t * 28f);
                data[n * each + i] = 0.5f * attack * decay * Mathf.Sin(2f * Mathf.PI * hertz[n] * t);
            }
        AudioClip c = AudioClip.Create(name, data.Length, 1, rate, false);
        c.SetData(data, 0);
        return c;
    }

    public static void play(string name, Vector3 position) {
        AudioClip c;
        if (!clips.TryGetValue(name, out c) || c == null) {
            if (name == "select")
                c = make(name, new float[] { 880f, 1320f }, 0.09f);
            else
                c = make(name, new float[] { 1500f }, 0.05f);
            clips[name] = c;
        }
        AudioSource.PlayClipAtPoint(c, position, 0.5f);
    }
}
