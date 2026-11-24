using System.Collections.Generic;
using UnityEngine;

// The look shared by the stroke panel and the message plate: dark glass,
// rounded corners, one warm accent.  Rounded rectangles are drawn into small
// textures once and reused.
public static class GlassStyle {

    public static readonly Color glass = new Color(0.065f, 0.08f, 0.115f, 0.95f);
    public static readonly Color glass_edge = new Color(0.40f, 0.44f, 0.52f, 0.95f);
    public static readonly Color accent = new Color(1f, 0.52f, 0.14f);

    const float pixels_per_meter = 900f;
    static Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();

    // White rounded rectangle, w by h meters, with the given corner radius;
    // soft > 0 makes a blurred glow of that reach, drawn into a texture that
    // is `soft` larger than the box on every side.
    public static Texture2D rounded(float w, float h, float radius, float soft) {
        string key = Mathf.RoundToInt(w * 10000f) + "x" + Mathf.RoundToInt(h * 10000f) + "r" + Mathf.RoundToInt(radius * 10000f)
                     + "s" + Mathf.RoundToInt(soft * 10000f);
        Texture2D t;
        if (textures.TryGetValue(key, out t) && t != null)
            return t;
        // Big shapes (a plate behind far away text) get fewer pixels per meter.
        float ppm = Mathf.Min(pixels_per_meter, 1024f / Mathf.Max(w + 2f * soft, h + 2f * soft));
        int pw = Mathf.Max(4, Mathf.RoundToInt((w + 2f * soft) * ppm));
        int ph = Mathf.Max(4, Mathf.RoundToInt((h + 2f * soft) * ppm));
        t = new Texture2D(pw, ph, TextureFormat.RGBA32, false);
        t.wrapMode = TextureWrapMode.Clamp;
        t.filterMode = FilterMode.Bilinear;
        float hx = 0.5f * w * ppm, hy = 0.5f * h * ppm;
        float r = Mathf.Min(radius * ppm, Mathf.Min(hx, hy));
        float reach = soft * ppm;
        Color32[] pixels = new Color32[pw * ph];
        for (int y = 0 ; y < ph ; ++y)
            for (int x = 0 ; x < pw ; ++x) {
                float px = Mathf.Abs(x + 0.5f - 0.5f * pw) - (hx - r);
                float py = Mathf.Abs(y + 0.5f - 0.5f * ph) - (hy - r);
                float d = Mathf.Sqrt(Mathf.Max(px, 0f) * Mathf.Max(px, 0f) + Mathf.Max(py, 0f) * Mathf.Max(py, 0f))
                          + Mathf.Min(Mathf.Max(px, py), 0f) - r;          // Distance outside the edge, in pixels.
                float a = (reach > 0f ? Mathf.Pow(Mathf.Clamp01(1f - d / reach), 2.2f)
                                      : Mathf.Clamp01(0.5f - d / 1.2f));
                pixels[y * pw + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(255f * a));
            }
        t.SetPixels32(pixels);
        t.Apply(false, true);
        textures[key] = t;
        return t;
    }

    // A flat quad with its own material: a texture, a color and a place in
    // the drawing order (see-through materials are sorted by distance, which
    // mixes up flat layers on one plane, so layers get a fixed order).
    public static Renderer quad(string name, Transform parent, Material source, Texture2D texture, Color c, int queue) {
        Material m = new Material(source);
        m.mainTexture = texture;
        m.color = c;
        m.renderQueue = queue;
        m.SetFloat("_Glossiness", 0f);
        m.SetFloat("_Metallic", 0f);
        Transform t = GhostVisuals.primitive(PrimitiveType.Quad, name, m, parent);
        return t.GetComponent<MeshRenderer>();
    }
}
