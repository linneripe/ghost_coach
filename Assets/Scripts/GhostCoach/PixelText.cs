using System.Collections.Generic;
using UnityEngine;

// The floating message (the billboard) drawn as a dot matrix display: white
// capital letters made of small round dots, wide apart, centred.  The
// billboard's own TextMesh still holds the text, so everything that writes
// to it works as before; its own drawing is turned off.
public class PixelText : MonoBehaviour {

    public float size_factor = 0.3f;          // One dot is the TextMesh's character size times this.
    public float letter_gap = 1.4f;          // Cells between letters.
    public float line_gap = 5f;              // Cells between lines.

    const int glyph_w = 5, glyph_h = 7, px = 8, tile_w = 6 * px, tile_h = 8 * px, columns = 16;
    static Texture2D atlas;
    static Dictionary<char, int> index = new Dictionary<char, int>();
    static int rows_in_atlas;

    TextMesh source;
    MeshFilter filter;
    MeshRenderer renderer_;
    Mesh mesh;
    string shown = null;
    Vector3 last_scale;
    public Vector2 world_size { get; private set; }     // Of the text, meters.
    public Vector3 local_center { get; private set; }   // In the billboard's local space.
    public bool has_text { get; private set; }
    public float cell_world { get; private set; }       // One dot, meters.

    public static PixelText attach(TextMesh t) {
        PixelText p = t.GetComponent<PixelText>();
        if (p == null) {
            p = t.gameObject.AddComponent<PixelText>();
            p.build(t);
        }
        return p;
    }

    void build(TextMesh t) {
        source = t;
        t.GetComponent<MeshRenderer>().enabled = false;
        GameObject g = new GameObject("pixel text");
        g.transform.SetParent(t.transform, false);
        filter = g.AddComponent<MeshFilter>();
        renderer_ = g.AddComponent<MeshRenderer>();
        Shader shader = Resources.Load<Shader>("GhostCoach/GhostUnlit");
        Material m = (shader != null ? new Material(shader) : new Material(GhostVisuals.material("ghost")));
        m.mainTexture = font_atlas();
        m.color = new Color(0.03f, 0.03f, 0.04f, 1f);
        m.renderQueue = 3020;
        renderer_.sharedMaterial = m;
        renderer_.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer_.receiveShadows = false;
        mesh = new Mesh();
        mesh.name = "pixel text";
        filter.sharedMesh = mesh;
    }

    void LateUpdate() {
        if (source == null)
            return;
        Vector3 s = transform.lossyScale;
        if (source.text != shown || s != last_scale) {
            shown = source.text;
            last_scale = s;
            rebuild(shown);
        }
    }

    void rebuild(string text) {
        mesh.Clear();
        has_text = false;
        if (string.IsNullOrEmpty(text) || last_scale.x < 1e-5f || last_scale.y < 1e-5f)
            return;
        float cell = source.characterSize * last_scale.x * size_factor;     // Meters, one dot.
        cell_world = cell;
        string[] lines = text.ToUpperInvariant().Split('\n');
        float advance = (glyph_w + letter_gap) * cell, line_height = (glyph_h + line_gap) * cell;
        float tile_x = 6f * cell, tile_y = 8f * cell;
        float ux = 1f / last_scale.x, uy = 1f / last_scale.y;
        float total_h = lines.Length * line_height - line_gap * cell;
        float widest = 0f;
        List<Vector3> v = new List<Vector3>();
        List<Vector2> uv = new List<Vector2>();
        List<int> tri = new List<int>();
        for (int l = 0 ; l < lines.Length ; ++l) {
            string line = lines[l];
            float w = line.Length * advance - letter_gap * cell;
            widest = Mathf.Max(widest, w);
            float y_top = 0.5f * total_h - l * line_height;                // Top of the glyph rows.
            for (int c = 0 ; c < line.Length ; ++c) {
                int g = glyph_index(line[c]);
                if (g < 0)
                    continue;                                              // A space.
                float x0 = -0.5f * w + c * advance - 0.5f * cell;          // The tile has half a cell of margin.
                float y1 = y_top + 0.5f * cell, y0 = y1 - tile_y;
                int at = v.Count;
                v.Add(new Vector3(x0 * ux, y0 * uy, 0f));
                v.Add(new Vector3((x0 + tile_x) * ux, y0 * uy, 0f));
                v.Add(new Vector3((x0 + tile_x) * ux, y1 * uy, 0f));
                v.Add(new Vector3(x0 * ux, y1 * uy, 0f));
                float u0 = (g % columns) * tile_w / (float)atlas.width, u1 = u0 + tile_w / (float)atlas.width;
                float v0 = (g / columns) * tile_h / (float)atlas.height, v1 = v0 + tile_h / (float)atlas.height;
                uv.Add(new Vector2(u0, v0)); uv.Add(new Vector2(u1, v0));
                uv.Add(new Vector2(u1, v1)); uv.Add(new Vector2(u0, v1));
                tri.AddRange(new int[] { at, at + 2, at + 1, at, at + 3, at + 2 });
            }
        }
        if (v.Count == 0)
            return;
        mesh.SetVertices(v);
        mesh.SetUVs(0, uv);
        mesh.SetTriangles(tri, 0);
        mesh.RecalculateBounds();
        has_text = true;
        world_size = new Vector2(widest, total_h);
        local_center = Vector3.zero;
    }

    // ---- the dot matrix font: 5 by 7 dots

    static int glyph_index(char c) {
        if (c == ' ')
            return -1;
        font_atlas();
        int g;
        if (index.TryGetValue(c, out g))
            return g;
        return index['?'];
    }

    // Seven rows of five dots for each character.
    static readonly string[] font = {
        "A 01110 10001 10001 11111 10001 10001 10001", "B 11110 10001 10001 11110 10001 10001 11110",
        "C 01110 10001 10000 10000 10000 10001 01110", "D 11110 10001 10001 10001 10001 10001 11110",
        "E 11111 10000 10000 11110 10000 10000 11111", "F 11111 10000 10000 11110 10000 10000 10000",
        "G 01110 10001 10000 10111 10001 10001 01111", "H 10001 10001 10001 11111 10001 10001 10001",
        "I 01110 00100 00100 00100 00100 00100 01110", "J 00111 00010 00010 00010 00010 10010 01100",
        "K 10001 10010 10100 11000 10100 10010 10001", "L 10000 10000 10000 10000 10000 10000 11111",
        "M 10001 11011 10101 10101 10001 10001 10001", "N 10001 11001 10101 10011 10001 10001 10001",
        "O 01110 10001 10001 10001 10001 10001 01110", "P 11110 10001 10001 11110 10000 10000 10000",
        "Q 01110 10001 10001 10001 10101 10010 01101", "R 11110 10001 10001 11110 10100 10010 10001",
        "S 01111 10000 10000 01110 00001 00001 11110", "T 11111 00100 00100 00100 00100 00100 00100",
        "U 10001 10001 10001 10001 10001 10001 01110", "V 10001 10001 10001 10001 10001 01010 00100",
        "W 10001 10001 10001 10101 10101 11011 10001", "X 10001 10001 01010 00100 01010 10001 10001",
        "Y 10001 10001 01010 00100 00100 00100 00100", "Z 11111 00001 00010 00100 01000 10000 11111",
        "Å 00100 01010 01110 10001 11111 10001 10001", "Ä 01010 00000 01110 10001 11111 10001 10001",
        "Ö 01010 00000 01110 10001 10001 10001 01110",
        "0 01110 10001 10011 10101 11001 10001 01110", "1 00100 01100 00100 00100 00100 00100 01110",
        "2 01110 10001 00001 00010 00100 01000 11111", "3 11110 00001 00001 01110 00001 00001 11110",
        "4 00010 00110 01010 10010 11111 00010 00010", "5 11111 10000 11110 00001 00001 10001 01110",
        "6 00110 01000 10000 11110 10001 10001 01110", "7 11111 00001 00010 00100 01000 01000 01000",
        "8 01110 10001 10001 01110 10001 10001 01110", "9 01110 10001 10001 01111 00001 00010 01100",
        ". 00000 00000 00000 00000 00000 01100 01100", ", 00000 00000 00000 00000 01100 00100 01000",
        ": 00000 01100 01100 00000 01100 01100 00000", "; 00000 01100 01100 00000 01100 00100 01000",
        "! 00100 00100 00100 00100 00100 00000 00100", "? 01110 10001 00001 00010 00100 00000 00100",
        "- 00000 00000 00000 11111 00000 00000 00000", "+ 00000 00100 00100 11111 00100 00100 00000",
        "/ 00001 00001 00010 00100 01000 10000 10000", "% 11001 11010 00010 00100 01000 01011 10011",
        "' 00100 00100 01000 00000 00000 00000 00000", "( 00010 00100 01000 01000 01000 00100 00010",
        ") 01000 00100 00010 00010 00010 00100 01000", "= 00000 00000 11111 00000 11111 00000 00000",
        "_ 00000 00000 00000 00000 00000 00000 11111", "< 00010 00100 01000 10000 01000 00100 00010",
        "> 01000 00100 00010 00001 00010 00100 01000",
    };

    // The font as a texture: one tile per character, each dot a small rounded square.
    static Texture2D font_atlas() {
        if (atlas != null)
            return atlas;
        rows_in_atlas = (font.Length + columns - 1) / columns;
        int w = columns * tile_w, h = rows_in_atlas * tile_h;
        Color32[] pixels = new Color32[w * h];
        for (int g = 0 ; g < font.Length ; ++g) {
            string[] parts = font[g].Split(' ');
            index[parts[0][0]] = g;
            int ox = (g % columns) * tile_w, oy = (g / columns) * tile_h;   // The tile's lower left corner.
            for (int y = 0 ; y < tile_h ; ++y)
                for (int x = 0 ; x < tile_w ; ++x) {
                    // Which dot is this pixel near?  Rows are counted from the top.
                    int cx = Mathf.FloorToInt((x - 0.5f * px) / px);
                    int cy = Mathf.FloorToInt(((tile_h - 1 - y) - 0.5f * px) / px);
                    if (cx < 0 || cx >= glyph_w || cy < 0 || cy >= glyph_h || parts[1 + cy][cx] != '1')
                        continue;
                    float dx = Mathf.Abs(x + 0.5f - (0.5f * px + (cx + 0.5f) * px)) - (0.5f * 6.2f - 1.8f);
                    float dy = Mathf.Abs((tile_h - 1 - y) + 0.5f - (0.5f * px + (cy + 0.5f) * px)) - (0.5f * 6.2f - 1.8f);
                    float d = Mathf.Sqrt(Mathf.Max(dx, 0f) * Mathf.Max(dx, 0f) + Mathf.Max(dy, 0f) * Mathf.Max(dy, 0f))
                              + Mathf.Min(Mathf.Max(dx, dy), 0f) - 1.8f;
                    byte a = (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(0.5f - d / 1.1f));
                    pixels[(oy + y) * w + ox + x] = new Color32(255, 255, 255, a);
                }
        }
        atlas = new Texture2D(w, h, TextureFormat.RGBA32, false);
        atlas.wrapMode = TextureWrapMode.Clamp;
        atlas.filterMode = FilterMode.Bilinear;
        atlas.SetPixels32(pixels);
        atlas.Apply(false, true);
        return atlas;
    }
}
