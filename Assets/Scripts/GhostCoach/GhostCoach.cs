using UnityEngine;
using UnityEngine.InputSystem;

// Training flow decided by the team:
//   Phase 1 Coach: the ghost coach plays at the table in the hitting
//     position while the player stands beside it, facing the same way,
//     and watches.
//   Phase 2 Path: the player plays in the hitting position with the
//     coach's paddle path shown as support, and gets a score and one tip
//     per stroke (PathGuide, StrokeCompare).
// The ball is always in play.  The player switches phase with a button
// (right controller A, P on the keyboard).  The right controller B button
// (K on the keyboard) starts and stops recording a coach clip.  Clicking
// the right thumbstick (L on the keyboard) changes the coach's playback
// speed.  Holding the left thumbstick in (N on the keyboard) starts a
// session for the next test participant (Experiment).
//
// Added at run time to the object holding the PlayerInput so it receives
// the On<Action> messages from PlayControls.inputactions.
public class GhostCoach : MonoBehaviour {

    public enum Phase { Coach, Path }

    public Play play;
    public MotionRecorder recorder;
    public CoachGhost coach_ghost;
    public PathGuide path_guide;
    public Table table;
    public Phase phase = Phase.Coach;
    public MotionClip recorded_clip;     // As recorded (or the mock coach).
    public MotionClip coach_clip;        // As shown: mirrored if the player uses the other hand.
    public Experiment experiment;
    bool player_left_handed;
    bool bindings_set = false;
    bool variant_applied = false;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void create() {
        Buttons buttons = FindFirstObjectByType<Buttons>();
        Play play = FindFirstObjectByType<Play>();
        Table table = FindFirstObjectByType<Table>();
        if (buttons == null || play == null || table == null)
            return;

        GameObject g = buttons.gameObject;
        MotionRecorder recorder = g.AddComponent<MotionRecorder>();
        recorder.enabled = false;         // Hook up references before OnEnable.
        recorder.play = play;
        recorder.table = table.transform;
        Camera head = (play.passthrough_camera != null ? play.passthrough_camera : Camera.main);
        recorder.head = (head != null ? head.transform : null);
        recorder.enabled = true;

        CoachGhost ghost = g.AddComponent<CoachGhost>();
        ghost.play = play;
        ghost.table = table.transform;

        PathGuide guide = g.AddComponent<PathGuide>();
        guide.enabled = false;            // Hook up references before OnEnable.
        guide.play = play;
        guide.table = table.transform;
        guide.enabled = true;

        GhostCoach coach = g.AddComponent<GhostCoach>();
        coach.play = play;
        coach.recorder = recorder;
        coach.coach_ghost = ghost;
        coach.path_guide = guide;
        coach.table = table;
        guide.stroke_scored += coach.stroke_scored;
    }

    void Start() {
        // A coach recorded on this headset, else a clip shipped with the app
        // (mocap), else a made-up forehand drive so both phases can be tried.
        MotionClip c = MotionClip.load_latest();
        if (c == null) {
            c = MotionClip.load_bundled();
            if (c != null)
                show_message("Coach: " + c.name + "\nA: byt fas   B: spela in egen coach");
        }
        if (c == null) {
            float ball_radius = (play.ball_in_play != null ? play.ball_in_play.radius : 0.02f);
            c = MockCoach.forehand_drive(table, ball_radius);
            show_message("Demo-coach (påhittad forehand)\nA: byt fas   B: spela in riktig coach");
        }
        Debug.Log("GhostCoach coach clip " + c.name + " (" + c.source + "), "
                  + c.contact_times().Count + " strokes");
        set_coach(c);
        experiment = new Experiment(this);
        experiment.start_session();
    }

    void OnDestroy() {
        if (experiment != null)
            experiment.end_session();
    }

    // Use the settings of the experiment's current variant.
    void apply_variant() {
        variant_applied = true;
        Variant v = experiment.variant;
        coach_ghost.stand_on_paddle_side = v.coach_on_paddle_side;
        coach_ghost.stand_behind = v.coach_behind;
        if (DesktopRig.active == null)        // The editor has no passthrough.
            play.enable_show_room(v.mixed_reality);
        update_ghost();
    }

    public void new_session() {
        experiment.start_session();
        apply_variant();
        show_message("Ny deltagare " + experiment.participant
                     + (experiment.finished ? "" : "\nBlock " + experiment.block_label));
        play.paddle_hand.wand.haptic_pulses(3, 0.08f, 0.8f, 0.08f);
    }

    // Use a clip as the coach, mirrored when the coach played with the
    // other hand than the player, so the player never has to mirror the
    // movement in their head.
    void set_coach(MotionClip c) {
        recorded_clip = c;
        if (player_left_handed != play.paddle_hand.wand.left || !bindings_set)
            mirror_controller_buttons(play.paddle_hand.wand.left);
        player_left_handed = play.paddle_hand.wand.left;
        if (c != null && c.left_handed != player_left_handed) {
            float top_y;
            coach_clip = c.mirrored(TableSpace.center(table, out top_y).x);
        } else
            coach_clip = c;
        update_ghost();
    }

    // The button bindings in PlayControls.inputactions assume a right
    // handed player: ball and serve on the left (free) hand.  For a left
    // handed player swap every left and right hand binding so the same
    // jobs stay on the same hand, except the menu button, which only the
    // left controller has.
    void mirror_controller_buttons(bool left_handed) {
        bindings_set = true;
        PlayerInput input = GetComponent<PlayerInput>();
        if (input == null || input.actions == null)
            return;
        foreach (InputAction action in input.actions) {
            for (int i = 0 ; i < action.bindings.Count ; ++i) {
                string path = action.bindings[i].path;
                bool handed = (path.Contains("{LeftHand}") || path.Contains("{RightHand}"));
                if (!handed || path.EndsWith("/start"))
                    continue;
                if (left_handed)
                    action.ApplyBindingOverride(i, path.Contains("{LeftHand}")
                                                ? path.Replace("{LeftHand}", "{RightHand}")
                                                : path.Replace("{RightHand}", "{LeftHand}"));
                else
                    action.RemoveBindingOverride(i);
            }
        }
    }

    // The player can switch hand in the settings menu at any time.
    void Update() {
        // After all Start() calls, so the settings menu does not undo it.
        if (!variant_applied && experiment != null)
            apply_variant();
        if (play.paddle_hand.wand.left != player_left_handed) {
            set_coach(recorded_clip);
            show_message(player_left_handed ? "Vänsterhänt: coachen spegelvänd" : "Högerhänt");
            experiment.log_event("hand", player_left_handed ? "left" : "right");
        }
    }

    public void set_phase(Phase p) {
        phase = p;
        update_ghost();
        if (coach_clip == null)
            show_message("Ingen coach inspelad än\nTryck B för att spela in");
        else
            show_message(phase == Phase.Coach
                         ? "Fas 1: Titta på coachen\nStå på den gröna markeringen"
                         : "Fas 2: Din tur\nStå på markeringen, följ banan");
        // Two short pulses, different from the single pulse of a ball hit.
        play.paddle_hand.wand.haptic_pulses(2, 0.04f, 0.6f, 0.08f);
        experiment.log_event("phase", phase == Phase.Coach ? "coach" : "path");
    }

    public void toggle_recording() {
        if (!recorder.recording) {
            recorder.start_recording();
            update_ghost();           // Hide the replay and the path, and do not score.
            show_message("Spelar in coach...\nTryck B igen för att sluta");
            play.paddle_hand.wand.haptic_pulses(1, 0.1f, 0.8f, 0f);
            experiment.log_event("recording_start", "");
        } else {
            MotionClip c = recorder.stop_recording();
            experiment.log_event("recording_stop", c != null ? c.name : "nothing recorded");
            if (c != null) {
                set_coach(c);
                show_message("Sparade " + c.name + "\n" + c.contact_times().Count + " slag, "
                             + c.duration.ToString("F0") + " s");
            } else
                show_message("Inget inspelat");
            play.paddle_hand.wand.haptic_pulses(3, 0.04f, 0.8f, 0.06f);
            if (c == null)
                update_ghost();
        }
    }

    // The coach ghost shows only in phase 1 and the path only in phase 2,
    // neither while recording.
    void update_ghost() {
        bool live = (coach_clip != null && !recorder.recording);
        if (live && phase == Phase.Coach)
            coach_ghost.show(coach_clip);
        else
            coach_ghost.hide();
        if (live && phase == Phase.Path)
            path_guide.show(coach_clip);
        else
            path_guide.hide();

        // Without a headset, move the view to where the player should stand.
        // In phase 1 turn halfway toward the coach, who is beside the player
        // and would be outside a flat screen's field of view.
        if (DesktopRig.active != null && live) {
            Vector3 forward = table.transform.forward;
            if (phase == Phase.Coach) {
                Vector3 to_coach = Vector3.ProjectOnPlane(coach_ghost.coach_position - coach_ghost.player_spot, Vector3.up);
                forward = (forward + to_coach.normalized).normalized;
                DesktopRig.active.stand_at(coach_ghost.player_spot, forward);
            } else
                DesktopRig.active.stand_at(path_guide.player_spot, forward);
        }
    }

    // Phase 2 feedback after each stroke, as the current variant says: the
    // score and one tip as short text, and/or a vibration in the free hand
    // so it does not mix with the ball hit pulse in the paddle hand.  One
    // long pulse means a good stroke, two short pulses mean look at the tip.
    public void stroke_scored(StrokeScore s) {
        Variant v = experiment.variant;
        show_message(v.feedback_text ? "Score " + s.score + "\n" + s.tip : "");
        if (v.feedback_haptic) {
            if (s.score >= StrokeCompare.good_score)
                play.free_hand.wand.haptic_pulses(1, 0.15f, 0.6f, 0f);
            else
                play.free_hand.wand.haptic_pulses(2, 0.05f, 0.8f, 0.08f);
        }
        string block_message = experiment.stroke(s, v.feedback_text, v.feedback_haptic);
        if (block_message != null) {
            apply_variant();
            show_message(block_message);
        }
    }

    public void change_speed() {
        float s = coach_ghost.next_speed();
        show_message("Coachens hastighet " + Mathf.RoundToInt(100f * s) + " %");
        experiment.log_event("coach_speed", Mathf.RoundToInt(100f * s) + "%");
    }

    void show_message(string text) {
        Debug.Log("GhostCoach: " + text.Replace("\n", " "));
        if (play.billboard != null)
            play.billboard.text = text;
    }

    // Input System messages (PlayControls.inputactions, PlayActions map).
    public void OnGhostTogglePhase() {
        set_phase(phase == Phase.Coach ? Phase.Path : Phase.Coach);
    }

    public void OnGhostRecord() {
        toggle_recording();
    }

    public void OnGhostSpeed() {
        change_speed();
    }

    public void OnGhostNewSession() {
        new_session();
    }
}
