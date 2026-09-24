using UnityEngine;

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
// speed.
//
// Added at run time to the object holding the PlayerInput so it receives
// the On<Action> messages from PlayControls.inputactions.
public class GhostCoach : MonoBehaviour {

    public enum Phase { Coach, Path }

    public Play play;
    public MotionRecorder recorder;
    public CoachGhost coach_ghost;
    public PathGuide path_guide;
    public Phase phase = Phase.Coach;
    public MotionClip coach_clip;

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
        guide.stroke_scored += coach.stroke_scored;
    }

    void Start() {
        coach_clip = MotionClip.load_latest();
        if (coach_clip != null)
            Debug.Log("GhostCoach loaded coach clip " + coach_clip.name + ", "
                      + coach_clip.contact_times().Count + " strokes");
        update_ghost();
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
    }

    public void toggle_recording() {
        if (!recorder.recording) {
            recorder.start_recording();
            coach_ghost.hide();       // Record live play, not the replay.
            show_message("Spelar in coach...\nTryck B igen för att sluta");
            play.paddle_hand.wand.haptic_pulses(1, 0.1f, 0.8f, 0f);
        } else {
            MotionClip c = recorder.stop_recording();
            if (c != null) {
                coach_clip = c;
                show_message("Sparade " + c.name + "\n" + c.contact_times().Count + " slag, "
                             + c.duration.ToString("F0") + " s");
            } else
                show_message("Inget inspelat");
            play.paddle_hand.wand.haptic_pulses(3, 0.04f, 0.8f, 0.06f);
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
    }

    // Phase 2 feedback after each stroke: the score and one tip as short
    // text, and a vibration in the free hand so it does not mix with the
    // ball hit pulse in the paddle hand.  One long pulse means a good
    // stroke, two short pulses mean look at the tip.
    public void stroke_scored(StrokeScore s) {
        show_message("Score " + s.score + "\n" + s.tip);
        if (s.score >= StrokeCompare.good_score)
            play.free_hand.wand.haptic_pulses(1, 0.15f, 0.6f, 0f);
        else
            play.free_hand.wand.haptic_pulses(2, 0.05f, 0.8f, 0.08f);
    }

    public void change_speed() {
        float s = coach_ghost.next_speed();
        show_message("Coachens hastighet " + Mathf.RoundToInt(100f * s) + " %");
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
}
