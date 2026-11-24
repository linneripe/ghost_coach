using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// The stroke menu: a panel hanging in the air beside the table, where it stays
// during the game.  It is shown when the game starts, so the player can choose
// a stroke at once.  The left Y button (M on the keyboard) starts and stops
// pointing: then a laser from the paddle hand controller points at a stroke
// and the trigger chooses it (left click with the mouse on the desktop).  The
// panel also has a button for the coach's speed, and lists what the buttons
// on the controllers do.
//
// Built in code, with no event system: the laser is intersected with the
// panel's plane and the buttons are rectangles on it.
public class StrokePanel : MonoBehaviour {

    public Play play;
    public GhostCoach coach;
    public float distance = 0.9f;          // Meters in front of the head.
    public float drop = 0.1f;              // Meters below the head.
    // The laser leaves the controller along its forward direction turned this
    // many degrees downward.  May need adjusting on the headset.
    public float pointer_pitch = 0f;

    // For tests: use this ray instead of the controller or the mouse.
    public bool use_ray = false;
    public Ray ray;

    public bool open { get; private set; }        // Pointing at the panel: laser and clicks.
    public int hover { get; private set; }

    public bool hanging {
        get { return root != null && root.gameObject.activeSelf; }
    }

    // A button: a rounded tile with a thin light border, and a soft glow
    // behind it that shows when it is the chosen stroke.
    class Item {
        public string name;
        public Rect rect;                  // Panel coordinates, meters.
        public bool enabled = true, selected = false, primary = false;
        public Renderer fill, border, glow;
        public TextMesh text, sub;         // The label and a small second text.
        public System.Action action;
    }

    const float width = 0.62f, height = 0.84f;
    List<Item> items = new List<Item>();
    List<Item> stroke_items = new List<Item>();
    Item speed_item;
    Renderer speed_fill;
    Rect speed_track;
    Transform root, dot;
    LineRenderer laser;
    Material source, laser_material, text_material;
    Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();
    TextMesh title;

    // Dark glass with one warm accent: the chosen stroke glows orange, like a
    // table-tennis ball, and so does the pointer.
    static readonly Color tile = new Color(0.15f, 0.17f, 0.23f);
    static readonly Color tile_border = new Color(0.28f, 0.31f, 0.38f);
    static readonly Color tile_hover = new Color(0.27f, 0.30f, 0.38f);
    static readonly Color tile_hover_border = new Color(0.62f, 0.66f, 0.74f);
    static readonly Color tile_selected = new Color(0.34f, 0.37f, 0.45f);
    static readonly Color tile_selected_border = new Color(0.80f, 0.82f, 0.88f);
    static readonly Color tile_off = new Color(0.10f, 0.115f, 0.15f);
    static readonly Color tile_off_border = new Color(0.17f, 0.19f, 0.24f);
    static readonly Color white_button = new Color(0.94f, 0.94f, 0.96f);
    static readonly Color ink = new Color(0.05f, 0.07f, 0.10f);
    static readonly Color muted = new Color(0.52f, 0.55f, 0.62f);
    static readonly Color soft_text = new Color(0.78f, 0.80f, 0.85f);
    static readonly Color accent = new Color(1f, 0.52f, 0.14f);

    public IEnumerable<string> button_names {
        get { foreach (Item i in items) yield return i.name; }
    }

    // Centre of a button in world coordinates (for tests).
    public Vector3 button_center(string name) {
        foreach (Item i in items)
            if (i.name == name)
                return root.TransformPoint(new Vector3(i.rect.center.x, i.rect.center.y, 0f));
        return root.position;
    }

    void Start() {
        hover = -1;
    }

    // Hang the panel in the air at a place, turned toward where the player
    // looks from.  It stays there.
    public void hang(Vector3 position, Vector3 viewed_from) {
        if (root == null)
            build();
        Vector3 forward = Vector3.ProjectOnPlane(position - viewed_from, Vector3.up);
        forward = (forward.sqrMagnitude < 0.01f ? Vector3.forward : forward.normalized);
        root.position = position;
        root.rotation = Quaternion.LookRotation(forward, Vector3.up);
        root.gameObject.SetActive(true);
        refresh();
    }

    // Start pointing at the panel.  If it is not hanging yet it is placed in
    // front of the player, where they are looking.
    public void show() {
        if (root == null || !hanging) {
            Camera head = (play.passthrough_camera != null ? play.passthrough_camera : Camera.main);
            Vector3 forward = Vector3.forward;
            Vector3 position = Vector3.zero;
            if (head != null) {
                forward = Vector3.ProjectOnPlane(head.transform.forward, Vector3.up);
                position = head.transform.position;
            }
            forward = (forward.sqrMagnitude < 0.01f ? Vector3.forward : forward.normalized);
            hang(position + distance * forward - drop * Vector3.up, position);
        }
        refresh();
        open = true;
        hover = -1;
        laser.gameObject.SetActive(true);
        dot.gameObject.SetActive(true);
    }

    // Stop pointing.  The panel keeps hanging.
    public void close() {
        open = false;
        hover = -1;
        if (root != null) {
            laser.gameObject.SetActive(false);
            dot.gameObject.SetActive(false);
            refresh();
        }
    }

    // Choose what the pointer is on.  Returns whether a button was pressed.
    public bool click() {
        if (!open || hover < 0 || hover >= items.Count || !items[hover].enabled)
            return false;
        items[hover].action();
        return true;
    }

    // Update the texts and colors from the coach's state.
    public void refresh() {
        if (root == null)
            return;
        for (int i = 0 ; i < stroke_items.Count ; ++i) {
            Item item = stroke_items[i];
            bool available = (i < coach.strokes.Count && coach.strokes[i].available);
            item.enabled = available;
            item.selected = (i == coach.stroke_choice);
            item.text.text = coach.strokes[i].label;
            item.sub.text = (available ? "" : "ingen tagning ännu");
            item.text.transform.localPosition = new Vector3(item.rect.center.x, item.rect.center.y + (available ? 0f : 0.014f), -0.004f);
        }
        title.text = (open ? "Välj slag" : "Slag");
        float speed = coach.coach_ghost.speed;
        speed_item.text.text = Mathf.RoundToInt(100f * speed) + " %";
        float w = Mathf.Max(0.012f, speed_track.width * Mathf.Clamp01(speed));
        speed_fill.transform.localPosition = new Vector3(speed_track.xMin + 0.5f * w, speed_track.center.y, 0.0005f);
        speed_fill.transform.localScale = new Vector3(w, speed_track.height, 1f);
        paint();
    }

    void paint() {
        for (int i = 0 ; i < items.Count ; ++i) {
            Item item = items[i];
            bool hovered = (i == hover && item.enabled);
            Color fill = tile, border = tile_border, text = Color.white;
            if (item.primary) {
                fill = (hovered ? Color.white : white_button);
                border = (hovered ? accent : white_button);
                text = ink;
            } else if (!item.enabled) {
                fill = tile_off;
                border = tile_off_border;
                text = muted;
            } else if (hovered) {
                fill = tile_hover;
                border = tile_hover_border;
            } else if (item.selected) {
                fill = tile_selected;
                border = tile_selected_border;
            }
            item.fill.material.color = fill;
            item.border.material.color = border;
            if (item.glow != null)
                item.glow.enabled = (item.selected && item.enabled);
            item.text.color = text;
            if (item.sub != null && item != speed_item)
                item.sub.color = text;
        }
    }

    void Update() {
        if (!open || !hanging)
            return;
        Ray r;
        if (!pointer(out r)) {
            laser.enabled = false;
            dot.gameObject.SetActive(false);
            return;
        }
        laser.enabled = true;
        int found = -1;
        float t;
        Vector3 end = r.origin + 2f * r.direction;
        Vector2 local;
        if (hit_panel(r, out t, out local)) {
            end = r.origin + t * r.direction;
            for (int i = 0 ; i < items.Count ; ++i)
                if (items[i].rect.Contains(local)) {
                    found = i;
                    break;
                }
        }
        if (found != hover) {
            hover = found;
            paint();
            if (found >= 0 && items[found].enabled)
                haptic(0.02f, 0.2f);
        }
        laser.positionCount = 2;
        laser.SetPosition(0, r.origin);
        laser.SetPosition(1, end);
        dot.gameObject.SetActive(true);
        dot.position = end;
    }

    // Where the ray meets the panel, in panel coordinates, if it does.
    bool hit_panel(Ray r, out float t, out Vector2 local) {
        t = 0f;
        local = Vector2.zero;
        float denominator = Vector3.Dot(root.forward, r.direction);
        if (Mathf.Abs(denominator) < 1e-5f)
            return false;
        t = Vector3.Dot(root.position - r.origin, root.forward) / denominator;
        if (t < 0f)
            return false;
        Vector3 p = root.InverseTransformPoint(r.origin + t * r.direction);
        local = new Vector2(p.x, p.y);
        return Mathf.Abs(p.x) <= 0.5f * width && Mathf.Abs(p.y) <= 0.5f * height;
    }

    // The ray the player points with: set by a test, the mouse on the
    // desktop, or the paddle hand controller.
    bool pointer(out Ray r) {
        r = ray;
        if (use_ray)
            return true;
        if (DesktopRig.active != null) {
            Camera head = (play.passthrough_camera != null ? play.passthrough_camera : Camera.main);
            if (head == null || Mouse.current == null)
                return false;
            r = head.ScreenPointToRay(Mouse.current.position.ReadValue());
            return true;
        }
        Vector3 p;
        Quaternion q;
        if (!play.paddle_hand.wand.pose(out p, out q))
            return false;
        r = new Ray(p, q * Quaternion.Euler(pointer_pitch, 0f, 0f) * Vector3.forward);
        return true;
    }

    void haptic(float duration, float strength) {
        play.paddle_hand.wand.haptic_pulse(duration, strength);
    }

    // ---- building the panel

    // ---- shapes: rounded rectangles drawn into small textures

    const float pixels_per_meter = 900f;

    // White rounded rectangle, w by h meters, with the given corner radius;
    // soft > 0 makes a blurred glow of that reach, drawn into a texture that
    // is `soft` larger than the box on every side.
    Texture2D rounded(float w, float h, float radius, float soft) {
        string key = Mathf.RoundToInt(w * 10000f) + "x" + Mathf.RoundToInt(h * 10000f) + "r" + Mathf.RoundToInt(radius * 10000f)
                     + "s" + Mathf.RoundToInt(soft * 10000f);
        Texture2D t;
        if (textures.TryGetValue(key, out t))
            return t;
        int pw = Mathf.Max(4, Mathf.RoundToInt((w + 2f * soft) * pixels_per_meter));
        int ph = Mathf.Max(4, Mathf.RoundToInt((h + 2f * soft) * pixels_per_meter));
        t = new Texture2D(pw, ph, TextureFormat.RGBA32, false);
        t.wrapMode = TextureWrapMode.Clamp;
        t.filterMode = FilterMode.Bilinear;
        float hx = 0.5f * w * pixels_per_meter, hy = 0.5f * h * pixels_per_meter, r = Mathf.Min(radius * pixels_per_meter, Mathf.Min(hx, hy));
        float reach = soft * pixels_per_meter;
        Color32[] pixels = new Color32[pw * ph];
        for (int y = 0 ; y < ph ; ++y)
            for (int x = 0 ; x < pw ; ++x) {
                float px = Mathf.Abs(x + 0.5f - 0.5f * pw) - (hx - r);
                float py = Mathf.Abs(y + 0.5f - 0.5f * ph) - (hy - r);
                float d = Mathf.Sqrt(Mathf.Max(px, 0f) * Mathf.Max(px, 0f) + Mathf.Max(py, 0f) * Mathf.Max(py, 0f))
                          + Mathf.Min(Mathf.Max(px, py), 0f) - r;          // Distance outside the edge, in pixels.
                float a = (reach > 0f ? Mathf.Pow(Mathf.Clamp01(1f - d / reach), 2.2f) * (d <= 0f ? 1f : 1f)
                                      : Mathf.Clamp01(0.5f - d / 1.2f));
                pixels[y * pw + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(255f * a));
            }
        t.SetPixels32(pixels);
        t.Apply(false, true);
        textures[key] = t;
        return t;
    }

    // A flat shape on the panel, centred at (x, y); negative z is toward the
    // player.  Each shape has its own material (a color, and a place in the
    // drawing order: the see-through materials are sorted by distance, which
    // mixes up flat layers on one plane, so the layers get a fixed order).
    Renderer shape(string name, Texture2D texture, float x, float y, float w, float h, Color c, int queue, float z) {
        Material m = new Material(source);
        m.mainTexture = texture;
        m.color = c;
        m.renderQueue = queue;
        m.SetFloat("_Glossiness", 0f);
        m.SetFloat("_Metallic", 0f);
        Transform t = GhostVisuals.primitive(PrimitiveType.Quad, name, m, root);
        t.localPosition = new Vector3(x, y, z);
        t.localScale = new Vector3(w, h, 1f);
        return t.GetComponent<MeshRenderer>();
    }

    Renderer rect_shape(string name, float x, float y, float w, float h, float radius, Color c, int queue, float z) {
        return shape(name, rounded(w, h, radius, 0f), x, y, w, h, c, queue, z);
    }

    // ---- building the panel

    void build() {
        source = GhostVisuals.material("ghost");
        laser_material = new Material(source);
        laser_material.color = accent;
        laser_material.renderQueue = 3030;

        root = new GameObject("StrokePanel").transform;
        // The glass: a thin light edge, then the dark surface.
        rect_shape("panel edge", 0f, 0f, width, height, 0.05f, new Color(0.40f, 0.44f, 0.52f, 0.95f), 3000, 0.0045f);
        rect_shape("panel surface", 0f, 0f, width - 0.006f, height - 0.006f, 0.047f, new Color(0.065f, 0.08f, 0.115f, 0.97f), 3001, 0.004f);

        title = make_text("Slag", new Vector3(-0.255f, 0.360f, -0.004f), 0.0044f);
        title.fontStyle = FontStyle.Bold;
        title.anchor = TextAnchor.MiddleLeft;
        title.alignment = TextAlignment.Left;
        divider(0.318f, 0.54f, 3003);

        // The four strokes in a grid: forehand on the paddle hand's side,
        // backhand on the other, the plain stroke over the drive.
        bool left_handed = (play.paddle_hand != null && play.paddle_hand.wand.left);
        for (int i = 0 ; i < coach.strokes.Count ; ++i) {
            int index = i;
            bool backhand = (i >= 2);
            float side = (backhand != left_handed ? -1f : 1f);    // Seen from the player.
            float x = side * 0.1375f;
            float y = (i % 2 == 0 ? 0.232f : 0.087f);
            Item item = make_tile("stroke " + coach.strokes[i].id, new Rect(x - 0.1275f, y - 0.065f, 0.255f, 0.13f),
                                  coach.strokes[i].label, 0.0036f, () => coach.choose_stroke(index));
            item.glow = shape("glow", rounded(0.255f, 0.13f, 0.024f, 0.03f), x, y, 0.255f + 0.06f, 0.13f + 0.06f,
                              new Color(accent.r, accent.g, accent.b, 0.55f), 3006, 0.003f);
            stroke_items.Add(item);
        }

        // Speed: the label, the value and a bar showing how fast the coach plays.
        // The next button is the way out.
        float row = -0.075f;
        speed_item = make_tile("speed", new Rect(-0.27f, row - 0.055f, 0.25f, 0.11f), "100 %", 0.0034f,
                               () => coach.change_speed());
        speed_item.text.anchor = TextAnchor.MiddleRight;
        speed_item.text.alignment = TextAlignment.Right;
        speed_item.text.fontStyle = FontStyle.Bold;
        speed_item.sub.text = "Hastighet";
        speed_item.sub.anchor = TextAnchor.MiddleLeft;
        speed_item.sub.alignment = TextAlignment.Left;
        speed_item.sub.color = soft_text;
        Rect r = speed_item.rect;
        speed_item.text.transform.localPosition = new Vector3(r.xMax - 0.016f, r.center.y + 0.021f, -0.004f);
        speed_item.sub.transform.localPosition = new Vector3(r.xMin + 0.016f, r.center.y + 0.021f, -0.004f);
        speed_track = new Rect(r.xMin + 0.016f, r.center.y - 0.027f, r.width - 0.032f, 0.012f);
        shape("speed track", rounded(speed_track.width, speed_track.height, 0.006f, 0f), speed_track.center.x, speed_track.center.y,
              speed_track.width, speed_track.height, new Color(0.30f, 0.33f, 0.40f), 3012, 0.0007f);
        speed_fill = shape("speed fill", rounded(0.2f, speed_track.height, 0.006f, 0f), speed_track.center.x, speed_track.center.y,
                           0.1f, speed_track.height, Color.white, 3013, 0.0005f);
        Item close = make_tile("close", new Rect(0.02f, row - 0.055f, 0.25f, 0.11f), "Klar", 0.0042f, () => coach.close_panel());
        close.primary = true;
        close.text.fontStyle = FontStyle.Bold;

        // What the buttons on the controllers do, and how to get the panel back.
        rect_shape("controls box", 0f, -0.2825f, 0.54f, 0.235f, 0.03f, new Color(0.035f, 0.045f, 0.07f, 1f), 3004, 0.0035f);
        TextMesh heading = make_text("KONTROLLER", new Vector3(-0.245f, -0.1975f, -0.004f), 0.0025f);
        heading.anchor = TextAnchor.MiddleLeft;
        heading.alignment = TextAlignment.Left;
        heading.color = muted;
        divider(-0.2175f, 0.49f, 3005);
        hint("Titta eller spela", "A", -0.1325f, -0.2575f, 0.255f);
        hint("Coach", "B", 0.1325f, -0.2575f, 0.255f);
        hint("Hastighet", "Spak", -0.1325f, -0.3135f, 0.255f);
        hint("Boll", "Avtryckare", 0.1325f, -0.3135f, 0.255f);
        hint("Menyn, byta slag", "Y", 0f, -0.3695f, 0.52f);

        laser = GhostVisuals.line("panel laser", laser_material, 0.003f, transform);
        dot = GhostVisuals.primitive(PrimitiveType.Sphere, "panel laser dot", laser_material, transform);
        dot.localScale = 0.02f * Vector3.one;     // A 40 mm ball would be 0.04.
        root.gameObject.SetActive(false);
        laser.gameObject.SetActive(false);
        dot.gameObject.SetActive(false);
    }

    void divider(float y, float w, int queue) {
        rect_shape("divider", 0f, y, w, 0.002f, 0f, new Color(1f, 1f, 1f, 0.10f), queue, 0.0035f);
    }

    // A row in the controls box: what it does on the left, the button as a
    // small pill on the right.
    void hint(string what, string button, float x, float y, float w) {
        rect_shape("hint " + what, x, y, w, 0.050f, 0.02f, new Color(0.085f, 0.10f, 0.14f, 1f), 3006, 0.003f);
        float pill_w = 0.036f + 0.0080f * button.Length;
        float pill_x = x + 0.5f * w - 0.010f - 0.5f * pill_w;
        rect_shape("pill " + button, pill_x, y, pill_w, 0.032f, 0.016f, new Color(0.24f, 0.27f, 0.34f, 1f), 3008, 0.0025f);
        TextMesh label = make_text(what, new Vector3(x - 0.5f * w + 0.014f, y, -0.004f), 0.0029f);
        label.anchor = TextAnchor.MiddleLeft;
        label.alignment = TextAlignment.Left;
        label.color = soft_text;
        TextMesh key = make_text(button, new Vector3(pill_x, y, -0.004f), 0.0027f);
        key.fontStyle = FontStyle.Bold;
    }

    // A button as a rounded tile with a light border.
    Item make_tile(string name, Rect rect, string label, float text_size, System.Action action) {
        Item item = new Item();
        item.name = name;
        item.rect = rect;
        item.action = action;
        const float edge = 0.003f;
        float radius = 0.024f;
        item.border = rect_shape(name + " border", rect.center.x, rect.center.y, rect.width + 2f * edge, rect.height + 2f * edge,
                                 radius + edge, tile_border, 3008, 0.002f);
        item.fill = rect_shape(name, rect.center.x, rect.center.y, rect.width, rect.height, radius, tile, 3010, 0.001f);
        item.text = make_text(label, new Vector3(rect.center.x, rect.center.y, -0.004f), text_size);
        item.sub = make_text("", new Vector3(rect.center.x, rect.center.y - 0.03f, -0.004f), 0.0024f);
        items.Add(item);
        return item;
    }

    // Text in the same font and material as the billboard, so it shows in
    // the headset build like the other texts.
    TextMesh make_text(string text, Vector3 local_position, float character_size) {
        GameObject g = new GameObject("text " + text);
        g.transform.SetParent(root, false);
        g.transform.localPosition = local_position;
        TextMesh t = g.AddComponent<TextMesh>();
        TextMesh reference = play.billboard;
        if (reference != null) {
            t.font = reference.font;
            if (text_material == null) {
                text_material = new Material(reference.GetComponent<MeshRenderer>().sharedMaterial);
                text_material.renderQueue = 3020;
            }
            g.GetComponent<MeshRenderer>().sharedMaterial = text_material;
        }
        t.text = text;
        t.anchor = TextAnchor.MiddleCenter;
        t.alignment = TextAlignment.Center;
        t.fontSize = 64;
        t.characterSize = character_size;
        t.color = Color.white;
        return t;
    }
}
