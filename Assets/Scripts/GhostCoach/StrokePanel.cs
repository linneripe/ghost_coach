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
        public MeshRenderer quad_renderer;
        public TextMesh text;
        public System.Action action;
    }

    const float width = 0.62f, height = 0.66f;
    List<Item> items = new List<Item>();
    List<Item> stroke_items = new List<Item>();
    Item speed_item, coach_item;
    Transform root, dot;
    LineRenderer laser;
    Material background_material, button_material, hover_material, selected_material, disabled_material;
    Material laser_material, text_material;
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
            item.text.text = coach.strokes[i].label + (available ? "" : "\n(data saknas)");
        }
        title.text = (open ? "Välj slag" : "Slag");
        speed_item.text.text = "Hastighet\n" + Mathf.RoundToInt(100f * coach.coach_ghost.speed) + " %";
        coach_item.text.text = (coach.coach_visible ? "Dölj\ncoach" : "Visa\ncoach");
        paint();
    }

    void paint() {
        for (int i = 0 ; i < items.Count ; ++i) {
            Item item = items[i];
            Material m = button_material;
            if (!item.enabled)
                m = disabled_material;
            else if (i == hover)
                m = hover_material;
            else if (item.selected)
                m = selected_material;
            item.quad_renderer.sharedMaterial = m;
            item.text.color = (item.enabled ? Color.white : new Color(0.7f, 0.7f, 0.7f));
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
        background_material = colored(ghost, new Color(0.06f, 0.09f, 0.16f, 0.97f), 3000);
        button_material = colored(ghost, new Color(0.22f, 0.28f, 0.42f, 1f), 3010);
        hover_material = colored(ghost, new Color(0.95f, 0.65f, 0.1f, 1f), 3010);
        selected_material = colored(ghost, new Color(0.0f, 0.62f, 0.72f, 1f), 3010);
        disabled_material = colored(ghost, new Color(0.14f, 0.14f, 0.16f, 1f), 3010);
        laser_material = colored(ghost, new Color(1f, 0.85f, 0.3f, 1f), 3030);

        root = new GameObject("StrokePanel").transform;
        Transform background = GhostVisuals.primitive(PrimitiveType.Quad, "panel background", background_material, root);
        background.localPosition = new Vector3(0f, 0f, 0.004f);
        background.localScale = new Vector3(width, height, 1f);

        title = make_text("Välj slag", new Vector3(0f, 0.285f, -0.004f), 0.0042f);
        title.fontStyle = FontStyle.Bold;

        // Four strokes, one button each.
        float y = 0.175f;
        for (int i = 0 ; i < coach.strokes.Count ; ++i) {
            int index = i;
            Item item = make_item("stroke " + coach.strokes[i].id, new Rect(-0.26f, y - 0.04f, 0.52f, 0.08f),
                                  coach.strokes[i].label, 0.0042f, () => coach.choose_stroke(index));
            stroke_items.Add(item);
            y -= 0.10f;
        }

        // Speed, show or hide the coach, close.
        float row = -0.245f;
        speed_item = make_item("speed", new Rect(-0.29f, row - 0.045f, 0.18f, 0.09f), "Hastighet", 0.0031f,
                               () => coach.change_speed());
        coach_item = make_item("coach", new Rect(-0.09f, row - 0.045f, 0.18f, 0.09f), "Dölj coach", 0.0031f,
                               () => coach.toggle_coach());
        make_item("close", new Rect(0.11f, row - 0.045f, 0.18f, 0.09f), "Klar", 0.0031f, () => coach.close_panel());

        laser = GhostVisuals.line("panel laser", laser_material, 0.004f, transform);
        dot = GhostVisuals.primitive(PrimitiveType.Sphere, "panel laser dot", laser_material, transform);
        dot.localScale = 0.015f * Vector3.one;
        root.gameObject.SetActive(false);
        laser.gameObject.SetActive(false);
        dot.gameObject.SetActive(false);
    }

    Item make_item(string name, Rect rect, string label, float text_size, System.Action action) {
        Item item = new Item();
        item.name = name;
        item.rect = rect;
        item.action = action;
        Transform quad = GhostVisuals.primitive(PrimitiveType.Quad, name, button_material, root);
        quad.localPosition = new Vector3(rect.center.x, rect.center.y, 0.001f);
        quad.localScale = new Vector3(rect.width, rect.height, 1f);
        item.quad = quad;
        item.quad_renderer = quad.GetComponent<MeshRenderer>();
        item.text = make_text(label, new Vector3(rect.center.x, rect.center.y, -0.004f), text_size);
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
