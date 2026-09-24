using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Plays the scene in the editor without a headset and checks that the
// desktop mouse and keyboard rig works.  Run from the command line with
//   Unity -batchmode -projectPath . -executeMethod DesktopSmokeTest.run
// Exit code 0 means all checks passed.
public static class DesktopSmokeTest {

    static int frame, phase;
    static float phase_start;
    static List<string> failures = new List<string>();
    static List<string> errors = new List<string>();
    static Vector3 paddle_start, ball_start;
    static Ball tossed_ball;
    static float toss_max_height;
    static int haptic_count_before;
    static Vector3 ghost_paddle_start;
    static float ghost_time_start;
    static int free_haptic_before;
    static bool saved_options_enabled;
    static EnterPlayModeOptions saved_options;

    [MenuItem("GhostCoach/Run desktop smoke test")]
    public static void run() {
        EditorSceneManager.OpenScene("Assets/scenes.unity");
        frame = 0;
        phase = 0;
        phase_start = 0f;
        failures.Clear();
        errors.Clear();
        // Keep static state across entering play mode.
        saved_options_enabled = EditorSettings.enterPlayModeOptionsEnabled;
        saved_options = EditorSettings.enterPlayModeOptions;
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
        Application.logMessageReceived += record_error;
        EditorApplication.update += step;
        EditorApplication.EnterPlaymode();
    }

    static void record_error(string message, string stack, LogType type) {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            errors.Add(type + ": " + message + "\n" + stack);
    }

    static void check(bool ok, string what) {
        Debug.Log("DesktopSmokeTest " + (ok ? "PASS " : "FAIL ") + what);
        if (!ok)
            failures.Add(what);
    }

    static void step() {
        if (!EditorApplication.isPlaying)
            return;
        frame += 1;
        DesktopRig rig = DesktopRig.active;
        Play play = Object.FindFirstObjectByType<Play>();

        // Advance through phases by game time, since batch mode does not
        // cap the frame rate and frames can be very short.
        float t = Time.time - phase_start;
        if (frame > 100000) {
            check(false, "game time advanced, only " + Time.time.ToString("F2") + " s after " + frame + " frames");
            finish();
            return;
        }
        if (phase == 0 && t > 0.5f) {
            check(rig != null, "desktop rig created when no headset");
            if (rig == null) { finish(); return; }
            rig.set_pointer(new Vector2(0.5f, 0.5f));
            next_phase();
        }
        else if (phase == 1 && t > 0.2f) {
            paddle_start = play.paddle_hand.held_paddle.transform.position;
            rig.set_pointer(new Vector2(0.8f, 0.3f));
            check(play.free_hand.holding_ball(), "ball held in free hand at start");
            tossed_ball = play.free_hand.held_ball;
            if (tossed_ball != null)
                ball_start = tossed_ball.transform.position;
            toss_max_height = 0f;
            rig.toss_ball();
            next_phase();
        }
        else if (phase == 2 && t <= 0.5f && tossed_ball != null) {
            toss_max_height = Mathf.Max(toss_max_height, tossed_ball.transform.position.y - ball_start.y);
        }
        else if (phase == 2) {
            Vector3 p = play.paddle_hand.held_paddle.transform.position;
            check((p - paddle_start).magnitude > 0.05f,
                  "paddle follows pointer, moved " + (p - paddle_start).magnitude.ToString("F2") + " m");
            check(!play.free_hand.holding_ball(), "ball released by toss");
            if (tossed_ball != null)
                check(toss_max_height > 0.15f,
                      "tossed ball rose " + toss_max_height.ToString("F2") + " m");
            rig.robot_serve();
            next_phase();
        }
        else if (phase == 3 && t > 0.1f) {
            ball_start = play.ball_in_play.transform.position;
            next_phase();
        }
        else if (phase == 4 && t > 0.5f) {
            float d = (play.ball_in_play.transform.position - ball_start).magnitude;
            check(d > 0.3f, "robot served ball moved " + d.ToString("F2") + " m");

            GhostCoach coach = Object.FindFirstObjectByType<GhostCoach>();
            check(coach != null, "GhostCoach created");
            if (coach == null) { finish(); return; }
            GhostCoach.Phase before = coach.phase;
            haptic_count_before = rig.paddle_haptic_count;
            coach.OnGhostTogglePhase();
            check(coach.phase != before, "phase toggled to " + coach.phase);
            coach.OnGhostRecord();
            check(coach.recorder.recording, "recording started");
            next_phase();
        }
        else if (phase == 5 && t > 0.5f) {
            // Move the paddle during the recording so the replay moves, and
            // mark a ball contact so the clip has a stroke to compare with.
            rig.set_pointer(new Vector2(0.3f, 0.6f));
            play.player_paddle_hit(play.ball_in_play);
            next_phase();
        }
        else if (phase == 6 && t > 0.5f) {
            GhostCoach coach = Object.FindFirstObjectByType<GhostCoach>();
            check(rig.paddle_haptic_count > haptic_count_before,
                  "haptic pulses shown in desktop mode (" + (rig.paddle_haptic_count - haptic_count_before) + ")");
            coach.OnGhostRecord();
            MotionClip c = coach.coach_clip;
            check(!coach.recorder.recording && c != null && c.frames.Count > 10,
                  "recording saved " + (c == null ? 0 : c.frames.Count) + " frames");
            if (c == null) { finish(); return; }
            MotionFrame m = c.sample(0.5f * c.duration);
            check(m.head_position.y > 0.5f,
                  "clip head height in table space " + m.head_position.y.ToString("F2") + " m");
            // Remove the test clip so it is not loaded as the coach later.
            System.IO.File.Delete(System.IO.Path.Combine(MotionClip.clips_directory(), c.name + ".json"));

            check(!coach.coach_ghost.showing, "coach ghost hidden in path phase");
            coach.OnGhostTogglePhase();
            check(coach.phase == GhostCoach.Phase.Coach && coach.coach_ghost.showing,
                  "coach ghost shown in coach phase");
            coach.coach_ghost.speed = 1f;
            GameObject gp = GameObject.Find("ghost paddle");
            check(gp != null, "ghost paddle created");
            if (gp != null)
                ghost_paddle_start = gp.transform.position;
            ghost_time_start = coach.coach_ghost.time;
            check(GameObject.Find("stand here marker") != null, "stand here marker created");
            next_phase();
        }
        else if (phase == 7 && t > 0.8f) {
            GhostCoach coach = Object.FindFirstObjectByType<GhostCoach>();
            check(coach.coach_ghost.time != ghost_time_start, "coach replay time advanced");
            GameObject gp = GameObject.Find("ghost paddle");
            if (gp != null) {
                float d = (gp.transform.position - ghost_paddle_start).magnitude;
                check(d > 0.05f, "ghost paddle replays recorded motion, moved " + d.ToString("F2") + " m");
                save_screenshot(gp.transform.position, "Logs/smoke_coach_phase.png");
            }
            int contacts = coach.coach_clip.contact_times().Count;
            check(contacts >= 1, "recorded clip has ball contacts (" + contacts + ")");

            coach.OnGhostTogglePhase();
            check(coach.phase == GhostCoach.Phase.Path && !coach.coach_ghost.showing
                  && coach.path_guide.showing, "path phase hides coach and shows path");
            check(coach.path_guide.coach_path_points > 10,
                  "coach paddle path drawn with " + coach.path_guide.coach_path_points + " points");
            free_haptic_before = rig.free_haptic_count;
            // Simulate the player hitting the ball.
            play.player_paddle_hit(play.ball_in_play);
            next_phase();
        }
        else if (phase == 8 && t > 0.6f) {
            GhostCoach coach = Object.FindFirstObjectByType<GhostCoach>();
            StrokeScore sc = coach.path_guide.last_score;
            check(sc != null && sc.score >= 0 && sc.score <= 100 && sc.tip.Length > 0,
                  "stroke scored " + (sc == null ? "none" : sc.score + " \"" + sc.tip + "\""));
            check(play.billboard.text.StartsWith("Score"), "score shown on billboard");
            check(rig.free_haptic_count > free_haptic_before, "score vibration in free hand");
            GameObject line = GameObject.Find("coach paddle path");
            if (line != null)
                save_screenshot(line.GetComponent<LineRenderer>().GetPosition(0), "Logs/smoke_path_phase.png");
            finish();
        }
    }

    // Render a view from beside and behind a point, for checking visuals
    // by eye.  Logs/ is not in git.
    static void save_screenshot(Vector3 target, string path) {
        Camera cam = new GameObject("smoke test camera").AddComponent<Camera>();
        Vector3 table_center = Object.FindFirstObjectByType<Table>().transform.position;
        Vector3 away = target - table_center;
        away.y = 0f;
        cam.transform.position = target + 1.8f * away.normalized + new Vector3(1.2f, 0.6f, 0f);
        cam.transform.LookAt(target - 0.3f * Vector3.up);
        cam.fieldOfView = 70f;
        RenderTexture rt = new RenderTexture(1280, 720, 24);
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        Texture2D image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
        image.Apply();
        RenderTexture.active = null;
        System.IO.File.WriteAllBytes(path, image.EncodeToPNG());
        Object.DestroyImmediate(cam.gameObject);
        Debug.Log("DesktopSmokeTest saved screenshot " + path);
    }

    static void next_phase() {
        phase += 1;
        phase_start = Time.time;
    }

    static void finish() {
        EditorApplication.update -= step;
        Application.logMessageReceived -= record_error;
        EditorApplication.ExitPlaymode();
        EditorSettings.enterPlayModeOptionsEnabled = saved_options_enabled;
        EditorSettings.enterPlayModeOptions = saved_options;

        foreach (string e in errors)
            Debug.Log("DesktopSmokeTest logged error during play: " + e);
        bool ok = (failures.Count == 0);
        Debug.Log("DesktopSmokeTest " + (ok ? "PASSED" : "FAILED") + ", "
                  + failures.Count + " failed checks, " + errors.Count + " errors logged, "
                  + frame + " frames, " + Time.time.ToString("F1") + " s game time");
        if (Application.isBatchMode)
            EditorApplication.Exit(ok ? 0 : 1);
    }
}
