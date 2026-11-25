using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// World-space visual dashboard for Ghost Coach, usable both in VR by touching
// buttons with the paddle (TouchUI) and on desktop by left-clicking (DesktopRig).
// Lets the player switch stroke modes (Forehand Drive, Backhand Drive, etc.),
// adjust ball and coach speed, and choose between Viewing Mode, Drill Mode, and Rally Mode.
public class GhostDashboard : MonoBehaviour {

    public GhostCoach coach;
    public Play play;
    public Table table;

    bool is_collapsed;
    public bool collapsed { get { return is_collapsed; } }
    public GameObject canvas_root { get; private set; }

    GameObject full_panel, collapsed_panel;
    Text status_text;
    Button path_toggle_button, hands_toggle_button;
    Text path_toggle_text, hands_toggle_text;

    readonly List<ModeButton> mode_buttons = new List<ModeButton>();
    readonly Dictionary<GhostCoach.TrainingMode, Button> training_buttons = new Dictionary<GhostCoach.TrainingMode, Button>();
    readonly List<Button> ball_speed_buttons = new List<Button>();
    readonly List<CoachSpeedButton> coach_speed_buttons = new List<CoachSpeedButton>();

    struct ModeButton {
        public MotionClip clip;
        public Button button;
    }

    struct CoachSpeedButton {
        public float speed;
        public Button button;
    }

    static readonly Color panel_bg = new Color(0.08f, 0.11f, 0.16f, 0.92f);
    static readonly Color active_color = new Color(0.16f, 0.56f, 0.86f, 1f);
    static readonly Color inactive_color = new Color(0.20f, 0.25f, 0.33f, 1f);
    static readonly Color action_color = new Color(0.25f, 0.31f, 0.40f, 1f);
    static readonly Color text_color = Color.white;
    static readonly Color muted_text_color = new Color(0.75f, 0.82f, 0.90f, 1f);

    Font cached_font;
    bool built = false;

    public void build_and_refresh() {
        if (!built)
            build();
        refresh();
    }

    public void toggle_collapsed() {
        apply_collapsed(!is_collapsed);
    }

    public void apply_collapsed(bool collapse) {
        is_collapsed = collapse;
        if (full_panel != null)
            full_panel.SetActive(!is_collapsed);
        if (collapsed_panel != null)
            collapsed_panel.SetActive(is_collapsed);
    }

    public void refresh() {
        if (!built || coach == null)
            return;

        string clip_title = MotionClip.display_name(coach.recorded_clip != null ? coach.recorded_clip.name : "");
        string mode_title = training_mode_label(coach.training_mode);
        if (status_text != null)
            status_text.text = clip_title + "  |  " + mode_title;

        foreach (var pair in training_buttons)
            set_button_active(pair.Value, pair.Key == coach.training_mode);

        foreach (ModeButton mb in mode_buttons) {
            bool selected = (coach.recorded_clip != null && mb.clip != null
                             && coach.recorded_clip.name == mb.clip.name);
            set_button_active(mb.button, selected);
        }

        for (int i = 0 ; i < ball_speed_buttons.Count ; ++i)
            set_button_active(ball_speed_buttons[i], i == coach.ball_speed_level);

        float cspeed = (coach.coach_ghost != null ? coach.coach_ghost.speed : 0.5f);
        foreach (CoachSpeedButton csb in coach_speed_buttons)
            set_button_active(csb.button, Mathf.Abs(cspeed - csb.speed) < 0.05f);

        if (path_toggle_text != null)
            path_toggle_text.text = "Path: " + (coach.show_path_in_drill ? "ON" : "OFF");
        if (path_toggle_button != null)
            set_button_active(path_toggle_button, coach.show_path_in_drill);

        bool hidden = (coach.hand_visuals != null && coach.hand_visuals.hidden);
        if (hands_toggle_text != null)
            hands_toggle_text.text = "Hands: " + (hidden ? "Hidden" : "Shown");
        if (hands_toggle_button != null)
            set_button_active(hands_toggle_button, hidden);
    }

    void build() {
        built = true;
        Transform parent = (table != null && table.transform.parent != null ? table.transform.parent : null);

        canvas_root = new GameObject("GhostCoach Dashboard");
        if (parent != null)
            canvas_root.transform.SetParent(parent, false);

        Canvas canvas = canvas_root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas_root.AddComponent<CanvasScaler>();
        canvas_root.AddComponent<GraphicRaycaster>();

        RectTransform rt = canvas_root.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(700f, 456f);
        rt.localScale = Vector3.one * 0.001f;

        if (table != null) {
            Vector3 world_pos = TableSpace.to_world(table.transform, new Vector3(-0.96f, 1.18f, -0.88f));
            Quaternion world_rot = table.transform.rotation * Quaternion.Euler(10f, 32f, 0f);
            canvas_root.transform.SetPositionAndRotation(world_pos, world_rot);
        }

        full_panel = new GameObject("Dashboard Panel");
        full_panel.transform.SetParent(canvas_root.transform, false);
        RectTransform full_rt = make_rect(full_panel, Vector2.zero, new Vector2(700f, 456f));
        full_rt.localPosition = Vector3.zero;
        Image bg = full_panel.AddComponent<Image>();
        bg.color = panel_bg;

        build_header(full_panel.transform);
        build_training_mode_row(full_panel.transform);
        build_game_modes_grid(full_panel.transform);
        build_ball_speed_row(full_panel.transform);
        build_coach_speed_row(full_panel.transform);
        build_actions_row(full_panel.transform);

        collapsed_panel = new GameObject("Dashboard Collapsed");
        collapsed_panel.transform.SetParent(canvas_root.transform, false);
        make_rect(collapsed_panel, new Vector2(-245f, 198f), new Vector2(180f, 44f));
        add_button(collapsed_panel.transform, "DashExpand", "Ghost Coach", Vector2.zero, new Vector2(180f, 44f),
                   active_color, 16, () => apply_collapsed(false));
        collapsed_panel.SetActive(false);
    }

    void build_header(Transform parent) {
        add_label(parent, "GHOST COACH", new Vector2(-180f, 202f), new Vector2(280f, 30f), 19, FontStyle.Bold, text_color);
        status_text = add_label(parent, "", new Vector2(-15f, 182f), new Vector2(610f, 24f), 13, FontStyle.Normal, muted_text_color);
        add_button(parent, "DashCollapse", "Hide", new Vector2(272f, 200f), new Vector2(96f, 34f),
                   action_color, 14, () => apply_collapsed(true));
    }

    void build_training_mode_row(Transform parent) {
        add_label(parent, "TRAINING MODE", new Vector2(-190f, 154f), new Vector2(260f, 22f), 12, FontStyle.Bold, muted_text_color);
        float w = 206f, h = 38f, y = 126f;
        training_buttons[GhostCoach.TrainingMode.ViewCoach] = add_button(
            parent, "ModeView", "View Coach", new Vector2(-218f, y), new Vector2(w, h),
            inactive_color, 15, () => coach.select_training_mode(GhostCoach.TrainingMode.ViewCoach));
        training_buttons[GhostCoach.TrainingMode.Drill] = add_button(
            parent, "ModeDrill", "Drill (Ball Feed)", new Vector2(0f, y), new Vector2(w, h),
            inactive_color, 15, () => coach.select_training_mode(GhostCoach.TrainingMode.Drill));
        training_buttons[GhostCoach.TrainingMode.Rally] = add_button(
            parent, "ModeRally", "Rally (Robot)", new Vector2(218f, y), new Vector2(w, h),
            inactive_color, 15, () => coach.select_training_mode(GhostCoach.TrainingMode.Rally));
    }

    void build_game_modes_grid(Transform parent) {
        add_label(parent, "GAME MODE / STROKE CLIP", new Vector2(-170f, 88f), new Vector2(300f, 22f), 12, FontStyle.Bold, muted_text_color);
        List<MotionClip> clips = new List<MotionClip>();
        if (coach != null && coach.available_clips != null)
            clips.AddRange(coach.available_clips);
        if (coach != null && coach.custom_recorded_clip != null && !clips.Exists(c => c.name == coach.custom_recorded_clip.name))
            clips.Add(coach.custom_recorded_clip);
        if (clips.Count == 0 && coach != null && coach.recorded_clip != null)
            clips.Add(coach.recorded_clip);

        int cols = 4;
        float w = 152f, h = 36f, gap_x = 10f, gap_y = 8f;
        float start_x = -0.5f * (cols - 1) * (w + gap_x);
        float top_y = 60f;
        int limit = Mathf.Min(clips.Count, 8);
        for (int i = 0 ; i < limit ; ++i) {
            MotionClip clip = clips[i];
            int r = i / cols;
            int c = i % cols;
            float x = start_x + c * (w + gap_x);
            float y = top_y - r * (h + gap_y);
            string label = MotionClip.display_name(clip.name);
            Button btn = add_button(parent, "Clip_" + clip.name, label, new Vector2(x, y), new Vector2(w, h),
                                    inactive_color, 13, () => coach.select_clip(clip));
            mode_buttons.Add(new ModeButton { clip = clip, button = btn });
        }
    }

    void build_ball_speed_row(Transform parent) {
        add_label(parent, "BALL SPEED (DRILL & ROBOT)", new Vector2(-170f, -26f), new Vector2(300f, 22f), 12, FontStyle.Bold, muted_text_color);
        float w = 206f, h = 36f, y = -54f;
        for (int i = 0 ; i < BallFeeder.speed_labels.Length ; ++i) {
            int level = i;
            float x = -218f + i * 218f;
            string text = BallFeeder.speed_labels[i] + " (" + BallFeeder.speed_presets[i].ToString("F1") + " m/s)";
            Button btn = add_button(parent, "BallSpeed_" + level, text, new Vector2(x, y), new Vector2(w, h),
                                    inactive_color, 14, () => coach.set_ball_speed(level));
            ball_speed_buttons.Add(btn);
        }
    }

    void build_coach_speed_row(Transform parent) {
        add_label(parent, "COACH PLAYBACK SPEED", new Vector2(-170f, -94f), new Vector2(300f, 22f), 12, FontStyle.Bold, muted_text_color);
        float[] speeds = { 0.25f, 0.50f, 1.0f };
        string[] labels = { "25%", "50%", "100%" };
        float w = 206f, h = 36f, y = -122f;
        for (int i = 0 ; i < speeds.Length ; ++i) {
            float spd = speeds[i];
            float x = -218f + i * 218f;
            Button btn = add_button(parent, "CoachSpeed_" + labels[i], labels[i], new Vector2(x, y), new Vector2(w, h),
                                    inactive_color, 14, () => coach.set_coach_speed(spd));
            coach_speed_buttons.Add(new CoachSpeedButton { speed = spd, button = btn });
        }
    }

    void build_actions_row(Transform parent) {
        add_label(parent, "QUICK ACTIONS", new Vector2(-190f, -162f), new Vector2(260f, 22f), 12, FontStyle.Bold, muted_text_color);
        float w = 152f, h = 38f, y = -192f;
        path_toggle_button = add_button(parent, "DashPathToggle", "Path: ON", new Vector2(-243f, y), new Vector2(w, h),
                                        active_color, 13, () => coach.set_drill_path_visible(!coach.show_path_in_drill));
        path_toggle_text = path_toggle_button.GetComponentInChildren<Text>();

        hands_toggle_button = add_button(parent, "DashHandsToggle", "Hands: Shown", new Vector2(-81f, y), new Vector2(w, h),
                                         action_color, 13, () => coach.toggle_hand_visuals());
        hands_toggle_text = hands_toggle_button.GetComponentInChildren<Text>();

        add_button(parent, "DashRobotServe", "Robot Serve", new Vector2(81f, y), new Vector2(w, h),
                   action_color, 13, () => {
                       if (play != null && play.robot != null)
                           play.robot.serve();
                   });
        add_button(parent, "DashReset", "Reset", new Vector2(243f, y), new Vector2(w, h),
                   action_color, 13, () => coach.reset());
    }

    Button add_button(Transform parent, string action_id, string label, Vector2 pos, Vector2 size,
                      Color bg_color, int font_size, UnityEngine.Events.UnityAction on_click) {
        GameObject go = new GameObject(action_id);
        go.transform.SetParent(parent, false);
        make_rect(go, pos, size);

        Image img = go.AddComponent<Image>();
        img.color = bg_color;

        Button btn = go.AddComponent<Button>();
        btn.targetGraphic = img;

        // BoxCollider enables both VR paddle TouchUI triggers and DesktopRig mouse raycasts.
        BoxCollider col = go.AddComponent<BoxCollider>();
        col.isTrigger = false;
        col.size = new Vector3(size.x, size.y, 24f);

        GameObject txt_go = new GameObject("Text");
        txt_go.transform.SetParent(go.transform, false);
        RectTransform txt_rt = make_rect(txt_go, Vector2.zero, size - new Vector2(8f, 4f));
        txt_rt.anchorMin = Vector2.zero;
        txt_rt.anchorMax = Vector2.one;
        txt_rt.offsetMin = new Vector2(4f, 2f);
        txt_rt.offsetMax = new Vector2(-4f, -2f);

        Text txt = txt_go.AddComponent<Text>();
        txt.text = label;
        txt.font = ui_font();
        txt.fontSize = font_size;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.color = text_color;

        btn.onClick.AddListener(() => {
            if (!ButtonDedup.accept("Dash_" + action_id))
                return;
            on_click();
        });
        return btn;
    }

    Text add_label(Transform parent, string text, Vector2 pos, Vector2 size, int font_size,
                   FontStyle style, Color color, TextAnchor align = TextAnchor.MiddleLeft) {
        GameObject go = new GameObject("Label_" + text);
        go.transform.SetParent(parent, false);
        make_rect(go, pos, size);
        Text txt = go.AddComponent<Text>();
        txt.text = text;
        txt.font = ui_font();
        txt.fontSize = font_size;
        txt.fontStyle = style;
        txt.alignment = align;
        txt.color = color;
        return txt;
    }

    static RectTransform make_rect(GameObject go, Vector2 pos, Vector2 size) {
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        rt.localScale = Vector3.one;
        return rt;
    }

    static void set_button_active(Button btn, bool active) {
        if (btn == null)
            return;
        Image img = btn.GetComponent<Image>();
        if (img != null)
            img.color = (active ? active_color : inactive_color);
    }

    static string training_mode_label(GhostCoach.TrainingMode mode) {
        switch (mode) {
        case GhostCoach.TrainingMode.ViewCoach: return "View Coach";
        case GhostCoach.TrainingMode.Drill: return "Drill (Ball Feed)";
        case GhostCoach.TrainingMode.Rally: return "Rally (Robot)";
        default: return "";
        }
    }

    Font ui_font() {
        if (cached_font != null)
            return cached_font;
        cached_font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (cached_font == null)
            cached_font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        return cached_font;
    }
}
