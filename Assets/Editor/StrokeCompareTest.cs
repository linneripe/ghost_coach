using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Checks the phase 2 stroke scoring with synthetic strokes whose correct
// result is known.  Run from the command line with
//   Unity -batchmode -quit -projectPath . -executeMethod StrokeCompareTest.run
// Exit code 0 means all checks passed.
public static class StrokeCompareTest {

    static List<string> failures = new List<string>();

    [MenuItem("GhostCoach/Run stroke scoring test")]
    public static void run() {
        failures.Clear();
        MotionClip coach = stroke(Vector3.zero, 0f, 1f);
        float contact = 0.5f;

        StrokeScore same = StrokeCompare.compare(coach, contact, coach, contact);
        check(same.score == 100 && same.tip == "Bra slag!", "identical stroke scores 100", same);

        // Same swing hit 30 cm to the side: only the contact offset changes.
        StrokeScore moved = StrokeCompare.compare(coach, contact, stroke(new Vector3(0.3f, 0f, 0f), 0f, 1f), contact);
        check(moved.score >= 99 && Mathf.Abs(moved.contact_offset - 0.3f) < 0.01f,
              "shifted stroke keeps score, offset 0.3 m", moved);

        // Paddle face 30 degrees more open (tilted upward).
        StrokeScore open = StrokeCompare.compare(coach, contact, stroke(Vector3.zero, 30f, 1f), contact);
        check(Mathf.Abs(open.face_angle - 30f) < 1f && open.score < StrokeCompare.good_score
              && open.tip == "Stäng racketen mer", "open face lowers score and says close", open);

        // Face 30 degrees more closed.
        StrokeScore closed = StrokeCompare.compare(coach, contact, stroke(Vector3.zero, -30f, 1f), contact);
        check(closed.tip == "Öppna racketen mer", "closed face says open", closed);

        // Half speed swing.
        StrokeScore slow = StrokeCompare.compare(coach, contact, stroke(Vector3.zero, 0f, 0.5f), contact);
        check(Mathf.Abs(slow.speed_ratio - 0.5f) < 0.05f && slow.score < 100,
              "half speed swing gives speed ratio 0.5", slow);
        check(slow.tip == "Sving snabbare", "half speed swing says swing faster", slow);

        StrokeScore fast = StrokeCompare.compare(coach, contact, stroke(Vector3.zero, 0f, 1.6f), contact);
        check(fast.tip == "Sving lugnare", "much faster swing says swing calmer", fast);

        // Scores stay in range for a very different stroke.
        StrokeScore wild = StrokeCompare.compare(coach, contact, stroke(new Vector3(0f, 0.5f, 0f), 70f, 3f), contact);
        check(wild.score >= 0 && wild.score < 40, "very different stroke scores low", wild);

        bool ok = (failures.Count == 0);
        Debug.Log("StrokeCompareTest " + (ok ? "PASSED" : "FAILED") + ", " + failures.Count + " failed checks");
        if (Application.isBatchMode)
            EditorApplication.Exit(ok ? 0 : 1);
    }

    static void check(bool ok, string what, StrokeScore s) {
        Debug.Log("StrokeCompareTest " + (ok ? "PASS " : "FAIL ") + what
                  + " (score " + s.score + ", shape " + s.shape_error.ToString("F3")
                  + " m, angle " + s.face_angle.ToString("F1") + ", speed " + s.speed_ratio.ToString("F2")
                  + ", offset " + s.contact_offset.ToString("F2") + " m, tip \"" + s.tip + "\")");
        if (!ok)
            failures.Add(what);
    }

    // Forehand drive-like swing: the paddle moves forward (+z) and up
    // through contact at t = 0.5 s, at the given speed factor, with the
    // face tilted open (positive) or closed (negative) by face_tilt degrees.
    static MotionClip stroke(Vector3 offset, float face_tilt, float speed) {
        MotionClip c = new MotionClip();
        for (int i = 0 ; i <= 100 ; ++i) {
            float t = i * 0.01f;
            float s = speed * (t - 0.5f);                 // Swing progress, 0 at contact.
            MotionFrame f = new MotionFrame();
            f.t = t;
            f.paddle_position = offset + new Vector3(0.2f * s, 0.9f + 0.6f * s, -1.4f + 2.5f * s);
            // Blade upright, forehand rubber facing +z, tilted about x.
            Quaternion upright = Quaternion.LookRotation(Vector3.back, Vector3.up);
            f.paddle_rotation = Quaternion.AngleAxis(-face_tilt, Vector3.right) * upright;
            f.head_rotation = Quaternion.identity;
            c.frames.Add(f);
        }
        return c;
    }
}
