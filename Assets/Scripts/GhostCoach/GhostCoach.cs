using UnityEngine;

// Training flow decided by the team:
//   Phase 1 Coach: the ghost coach plays at the table in the hitting
//     position while the player stands beside it, facing the same way,
//     and watches.
//   Phase 2 Path: the player plays in the hitting position with the
//     coach's paddle path shown as support, and gets a score per stroke.
// The ball is always in play.  The player switches phase with a button
// (right controller A, P on the keyboard).  The right controller B button
// (K on the keyboard) starts and stops recording a coach clip.
//
// Added at run time to the object holding the PlayerInput so it receives
// the On<Action> messages from PlayControls.inputactions.
public class GhostCoach : MonoBehaviour {

    public enum Phase { Coach, Path }

    public Play play;
    public MotionRecorder recorder;
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

        GhostCoach coach = g.AddComponent<GhostCoach>();
        coach.play = play;
        coach.recorder = recorder;
    }

    void Start() {
        coach_clip = MotionClip.load_latest();
        if (coach_clip != null)
            Debug.Log("GhostCoach loaded coach clip " + coach_clip.name + ", "
                      + coach_clip.contact_times().Count + " strokes");
    }

    public void set_phase(Phase p) {
        phase = p;
        show_message(phase == Phase.Coach
                     ? "Phase 1: Watch the coach\nStand beside the table"
                     : "Phase 2: Your turn\nFollow the coach's path");
        // Two short pulses, different from the single pulse of a ball hit.
        play.paddle_hand.wand.haptic_pulses(2, 0.04f, 0.6f, 0.08f);
    }

    public void toggle_recording() {
        if (!recorder.recording) {
            recorder.start_recording();
            show_message("Recording coach...\nPress B again to stop");
            play.paddle_hand.wand.haptic_pulses(1, 0.1f, 0.8f, 0f);
        } else {
            MotionClip c = recorder.stop_recording();
            if (c != null) {
                coach_clip = c;
                show_message("Saved " + c.name + "\n" + c.contact_times().Count + " strokes, "
                             + c.duration.ToString("F0") + " s");
            } else
                show_message("Nothing recorded");
            play.paddle_hand.wand.haptic_pulses(3, 0.04f, 0.8f, 0.06f);
        }
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
}
