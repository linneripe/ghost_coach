using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// A recorded motion, e.g. a coach playing a few strokes, used both for the
// coach ghost (phase 1) and the paddle path the player follows (phase 2).
//
// All poses are in table coordinates (see TableSpace) so a clip recorded
// at one real table plays back at the right place at another table after
// the virtual table has been aligned with "move table".  Clips come from
// the Quest (MotionRecorder) or later from Qualisys exports.
[Serializable]
public class MotionClip {
    public string name;
    public string source;            // "quest" or "qualisys"
    public bool left_handed;
    public string recorded;          // Date and time, ISO 8601.
    public List<MotionFrame> frames = new List<MotionFrame>();
    public List<MotionEvent> events = new List<MotionEvent>();

    public float duration {
        get { return (frames.Count > 0 ? frames[frames.Count-1].t : 0f); }
    }

    // Times of ball contacts with the paddle, seconds from clip start.
    public List<float> contact_times() {
        List<float> times = new List<float>();
        foreach (MotionEvent e in events)
            if (e.type == "contact")
                times.Add(e.t);
        return times;
    }

    // Interpolated frame at time t, clamped to the clip.
    public MotionFrame sample(float t) {
        int n = frames.Count;
        if (n == 0)
            return null;
        if (t <= frames[0].t)
            return frames[0];
        if (t >= frames[n-1].t)
            return frames[n-1];

        // Binary search for the frames around t.
        int lo = 0, hi = n - 1;
        while (hi - lo > 1) {
            int mid = (lo + hi) / 2;
            if (frames[mid].t <= t)
                lo = mid;
            else
                hi = mid;
        }
        MotionFrame a = frames[lo], b = frames[hi];
        float f = (b.t > a.t ? (t - a.t) / (b.t - a.t) : 0f);
        return MotionFrame.lerp(a, b, f);
    }

    // Copy mirrored left to right about the plane x = center_x in table
    // coordinates, so a right handed coach becomes a left handed one.
    public MotionClip mirrored(float center_x) {
        MotionClip m = new MotionClip();
        m.name = name + "_mirrored";
        m.source = source;
        m.left_handed = !left_handed;
        m.recorded = recorded;
        foreach (MotionFrame f in frames)
            m.frames.Add(f.mirrored(center_x));
        m.events.AddRange(events);
        return m;
    }

    public static string clips_directory() {
        return Path.Combine(Application.persistentDataPath, "GhostCoach", "clips");
    }

    public string save() {
        string dir = clips_directory();
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, name + ".json");
        File.WriteAllText(path, JsonUtility.ToJson(this));
        Debug.Log("Saved motion clip " + name + " (" + frames.Count + " frames, "
                  + duration.ToString("F1") + " s) to " + path);
        return path;
    }

    public static MotionClip load(string path) {
        if (!File.Exists(path))
            return null;
        return JsonUtility.FromJson<MotionClip>(File.ReadAllText(path));
    }

    // Most recently written clip, or null if there are none.
    public static MotionClip load_latest() {
        string dir = clips_directory();
        if (!Directory.Exists(dir))
            return null;
        string latest = null;
        DateTime latest_time = DateTime.MinValue;
        foreach (string path in Directory.GetFiles(dir, "*.json")) {
            DateTime t = File.GetLastWriteTime(path);
            if (t > latest_time) {
                latest = path;
                latest_time = t;
            }
        }
        return (latest == null ? null : load(latest));
    }
}

[Serializable]
public class MotionFrame {
    public float t;                        // Seconds from clip start.
    public Vector3 head_position;          // Table coordinates, meters.
    public Quaternion head_rotation;
    public Vector3 paddle_position;        // Paddle blade center.
    public Quaternion paddle_rotation;
    public bool ball_in_play;
    public Vector3 ball_position;
    public Vector3 robot_paddle_position;  // Opponent, so a rally can be replayed.
    public Quaternion robot_paddle_rotation;

    public MotionFrame mirrored(float center_x) {
        MotionFrame m = new MotionFrame();
        m.t = t;
        m.head_position = mirror(head_position, center_x);
        m.head_rotation = mirror(head_rotation);
        m.paddle_position = mirror(paddle_position, center_x);
        m.paddle_rotation = mirror(paddle_rotation);
        m.ball_in_play = ball_in_play;
        m.ball_position = mirror(ball_position, center_x);
        m.robot_paddle_position = mirror(robot_paddle_position, center_x);
        m.robot_paddle_rotation = mirror(robot_paddle_rotation);
        return m;
    }

    static Vector3 mirror(Vector3 p, float center_x) {
        return new Vector3(2f * center_x - p.x, p.y, p.z);
    }

    // Rotation seen in a mirror across the x = 0 plane (same trick as the
    // left handed grip in Grip.hand_to_paddle_motion).
    static Quaternion mirror(Quaternion q) {
        return new Quaternion(q.x, -q.y, -q.z, q.w);
    }

    public static MotionFrame lerp(MotionFrame a, MotionFrame b, float f) {
        MotionFrame m = new MotionFrame();
        m.t = Mathf.Lerp(a.t, b.t, f);
        m.head_position = Vector3.Lerp(a.head_position, b.head_position, f);
        m.head_rotation = Quaternion.Slerp(a.head_rotation, b.head_rotation, f);
        m.paddle_position = Vector3.Lerp(a.paddle_position, b.paddle_position, f);
        m.paddle_rotation = Quaternion.Slerp(a.paddle_rotation, b.paddle_rotation, f);
        m.ball_in_play = (f < 0.5f ? a.ball_in_play : b.ball_in_play);
        m.ball_position = Vector3.Lerp(a.ball_position, b.ball_position, f);
        m.robot_paddle_position = Vector3.Lerp(a.robot_paddle_position, b.robot_paddle_position, f);
        m.robot_paddle_rotation = Quaternion.Slerp(a.robot_paddle_rotation, b.robot_paddle_rotation, f);
        return m;
    }
}

[Serializable]
public class MotionEvent {
    public float t;
    public string type;        // "contact" when the ball hits the paddle.
}

// Converts between world and table coordinates.  Ignores the table
// transform's scale so distances stay in meters.
public static class TableSpace {
    public static Vector3 to_table(Transform table, Vector3 world_position) {
        return Quaternion.Inverse(table.rotation) * (world_position - table.position);
    }

    public static Quaternion to_table(Transform table, Quaternion world_rotation) {
        return Quaternion.Inverse(table.rotation) * world_rotation;
    }

    public static Vector3 to_world(Transform table, Vector3 table_position) {
        return table.position + table.rotation * table_position;
    }

    public static Quaternion to_world(Transform table, Quaternion table_rotation) {
        return table.rotation * table_rotation;
    }

    // Middle of the table surface projected to the floor, and the surface
    // height, in table coordinates.
    public static Vector3 center(Table table, out float top_y) {
        Transform top = table.table_top.transform;
        Vector3 c = to_table(table.transform, top.position);
        top_y = c.y + 0.5f * top.lossyScale.y;
        c.y = 0f;
        return c;
    }
}
