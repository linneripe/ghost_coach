using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// The stroke menu: a panel hanging in the air beside the table, where it stays
// during the game.  It is shown when the game starts, so the player can choose
// a stroke at once.  The left Y button (M on the keyboard) starts and stops
// pointing: then a laser from the paddle hand controller points at a stroke
// and the trigger chooses it (left click with the mouse on the desktop).  The
// panel also has buttons for the coach's speed and for hiding or showing the
// coach.
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

    class Item {
        public string name;
        public Rect rect;                  // Panel coordinates, meters.
        public bool enabled = true, selected = false;
        public Transform quad;
        public MeshRenderer quad_renderer, outline_renderer;
        public TextMesh text, sub;         // The label and a small line under it.
        public float text_y;               // Where the label sits when there is no small line.
        public System.Action action;
    }

    const float width = 0.62f, height = 0.66f;
    List<Item> items = new List<Item>();
    List<Item> stroke_items = new List<Item>();
    Item speed_item, coach_item;
    Transform root, dot;
    LineRenderer laser;
    Material frame_material, background_material, line_material, button_material, outline_material;
    Material hover_material, selected_material, disabled_material;
    Material laser_material, text_material;

    // The panel is a little table-tennis table: dark blue playing surface, a
    // white painted edge line, a white centre line between forehand and
    // backhand, and the orange ball as the pointer and the choice.
    static readonly Color table_blue = new Color(0.04f, 0.17f, 0.24f);
    static readonly Color paint_white = new Color(0.95f, 0.95f, 0.90f);
    static readonly Color ball_orange = new Color(1f, 0.47f, 0.08f);
    static readonly Color ink = new Color(0.04f, 0.10f, 0.14f);
    static readonly Color muted = new Color(0.45f, 0.58f, 0.63f);
    TextMesh title;

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
        speed_item.text.text = Mathf.RoundToInt(100f * coach.coach_ghost.speed) + " %";
        speed_item.sub.text = "hastighet";
        coach_item.text.text = (coach.coach_visible ? "Dölj" : "Visa");
        coach_item.sub.text = "coach";
        paint();
    }

    void paint() {
        for (int i = 0 ; i < items.Count ; ++i) {
            Item item = items[i];
            Material m = button_material;
            Color text = Color.white;
            if (!item.enabled) {
                m = disabled_material;
                text = muted;
            } else if (i == hover) {
                m = hover_material;
                text = ink;
            } else if (item.selected) {
                m = selected_material;
                text = ink;
            }
            item.quad_renderer.sharedMaterial = m;
            item.outline_renderer.enabled = item.enabled;
            item.text.color = text;
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

    // A copy of a material with a color and a place in the drawing order.  The
    // see-through materials are sorted by distance, which mixes up flat layers
    // on one plane, so the layers get a fixed order: background, buttons,
    // text, laser.
    Material colored(Material source, Color c, int queue) {
        Material m = new Material(source);
        m.color = c;
        m.renderQueue = queue;
        return m;
    }

    void build() {
        Material ghost = GhostVisuals.material("ghost");
        frame_material = colored(ghost, paint_white, 3000);
        background_material = colored(ghost, table_blue, 3001);
        line_material = colored(ghost, new Color(paint_white.r, paint_white.g, paint_white.b, 0.55f), 3004);
        outline_material = colored(ghost, new Color(0.40f, 0.60f, 0.68f, 1f), 3008);
        button_material = colored(ghost, new Color(0.08f, 0.30f, 0.40f, 1f), 3010);
        hover_material = colored(ghost, paint_white, 3010);
        selected_material = colored(ghost, ball_orange, 3010);
        disabled_material = colored(ghost, new Color(0.05f, 0.14f, 0.19f, 1f), 3010);
        laser_material = colored(ghost, ball_orange, 3030);

        root = new GameObject("StrokePanel").transform;
        // The white edge line, then the surface inside it.
        flat("panel edge", frame_material, 0f, 0f, width, height, 0.0045f);
        flat("panel surface", background_material, 0f, 0f, width - 0.016f, height - 0.016f, 0.004f);

        title = make_text("Välj slag", new Vector3(0f, 0.270f, -0.004f), 0.0046f);
        title.fontStyle = FontStyle.Bold;
        flat("title line", line_material, 0f, 0.222f, 0.52f, 0.003f, 0.0035f);

        // The four strokes in a grid: forehand on the paddle hand's side, backhand
        // on the other, the plain stroke over the drive, with the centre line between.
        bool left_handed = (play.paddle_hand != null && play.paddle_hand.wand.left);
        for (int i = 0 ; i < coach.strokes.Count ; ++i) {
            int index = i;
            bool backhand = (i >= 2);
            float side = (backhand != left_handed ? -1f : 1f);    // Seen from the player.
            float x = side * 0.1425f;
            float y = (i % 2 == 0 ? 0.125f : -0.045f);
            Item item = make_item("stroke " + coach.strokes[i].id, new Rect(x - 0.1225f, y - 0.07f, 0.245f, 0.14f),
                                  coach.strokes[i].label, 0.0036f, () => coach.choose_stroke(index));
            stroke_items.Add(item);
        }
        flat("centre line", line_material, 0f, 0.040f, 0.003f, 0.34f, 0.0035f);
        flat("net line", line_material, 0f, -0.140f, 0.52f, 0.003f, 0.0035f);

        // Speed, show or hide the coach, done.
        float row = -0.230f;
        speed_item = make_item("speed", new Rect(-0.27f, row - 0.05f, 0.17f, 0.10f), "100 %", 0.0042f,
                               () => coach.change_speed());
        coach_item = make_item("coach", new Rect(-0.085f, row - 0.05f, 0.17f, 0.10f), "Dölj", 0.0042f,
                               () => coach.toggle_coach());
        make_item("close", new Rect(0.10f, row - 0.05f, 0.17f, 0.10f), "Klar", 0.0042f, () => coach.close_panel());

        laser = GhostVisuals.line("panel laser", laser_material, 0.003f, transform);
        dot = GhostVisuals.primitive(PrimitiveType.Sphere, "panel laser dot", laser_material, transform);
        dot.localScale = 0.02f * Vector3.one;     // A 40 mm ball would be 0.04.
        root.gameObject.SetActive(false);
        laser.gameObject.SetActive(false);
        dot.gameObject.SetActive(false);
    }

    // A flat rectangle on the panel, centred at (x, y), z toward the player is negative.
    Transform flat(string name, Material m, float x, float y, float w, float h, float z) {
        Transform t = GhostVisuals.primitive(PrimitiveType.Quad, name, m, root);
        t.localPosition = new Vector3(x, y, z);
        t.localScale = new Vector3(w, h, 1f);
        return t;
    }

    Item make_item(string name, Rect rect, string label, float text_size, System.Action action) {
        Item item = new Item();
        item.name = name;
        item.rect = rect;
        item.action = action;
        const float edge = 0.004f;            // Thin light outline around each button.
        Transform outline = flat(name + " outline", outline_material, rect.center.x, rect.center.y,
                                 rect.width + 2f * edge, rect.height + 2f * edge, 0.002f);
        item.outline_renderer = outline.GetComponent<MeshRenderer>();
        Transform quad = flat(name, button_material, rect.center.x, rect.center.y, rect.width, rect.height, 0.001f);
        item.quad = quad;
        item.quad_renderer = quad.GetComponent<MeshRenderer>();
        item.text_y = rect.center.y + 0.012f;
        item.text = make_text(label, new Vector3(rect.center.x, item.text_y, -0.004f), text_size);
        item.sub = make_text("", new Vector3(rect.center.x, rect.center.y - 0.030f, -0.004f), 0.0024f);
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
