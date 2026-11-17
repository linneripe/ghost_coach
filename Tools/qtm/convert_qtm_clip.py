#!/usr/bin/env python3
"""Convert a Qualisys Track Manager (QTM) JSON export to a GhostCoach MotionClip JSON.

Usage:
    python Tools/qtm/convert_qtm_clip.py pro-mocap-data/backhand_drive0009.json \
        Assets/Resources/GhostCoach/clips/backhand_drive0009.json

Options:
    --name NAME                 Clip name (default: output file stem)
    --start S --end E           Seconds of the take to export (default: full take)
    --left                      Player is left-handed
    --min-speed V               Racket speed peak in m/s that counts as a stroke (default: 2.0)
    --table-markers L R         Names of left and right table corner markers (default: TableL TableR)
    --racket-markers M1 ...     Names of racket rim markers, starting with topmost/thumb-side (default: Racket1..Racket5)
"""

import argparse
import json
import math
import os
import sys
import numpy as np

OUTPUT_RATE = 120.0
TABLE_HALF_LENGTH = 1.37
MIN_STROKE_GAP = 0.5

BODY_JOINTS = [
    "Hips", "Spine2", "Neck", "Head",
    "LeftArm", "LeftForeArm", "LeftHand",
    "RightArm", "RightForeArm", "RightHand",
    "LeftUpLeg", "LeftLeg", "LeftFoot", "LeftToeBase",
    "RightUpLeg", "RightLeg", "RightFoot", "RightToeBase",
]

DEFAULT_TABLE_MARKERS = ("TableL", "TableR")
DEFAULT_RACKET_MARKERS = ("Racket1", "Racket2", "Racket3", "Racket4", "Racket5")


def quat_to_matrix(qx, qy, qz, qw):
    """Convert a quaternion (x, y, z, w) to a 3x3 rotation matrix."""
    return np.array([
        [1.0 - 2.0 * (qy * qy + qz * qz), 2.0 * (qx * qy - qz * qw),       2.0 * (qx * qz + qy * qw)],
        [2.0 * (qx * qy + qz * qw),       1.0 - 2.0 * (qx * qx + qz * qz), 2.0 * (qy * qz - qx * qw)],
        [2.0 * (qx * qz - qy * qw),       2.0 * (qy * qz + qx * qw),       1.0 - 2.0 * (qx * qx + qy * qy)],
    ], dtype=float)


def quaternion_from_axes(x, y, z):
    """Build a Unity quaternion dict from orthonormal local axes x, y, z."""
    m00, m01, m02 = float(x[0]), float(y[0]), float(z[0])
    m10, m11, m12 = float(x[1]), float(y[1]), float(z[1])
    m20, m21, m22 = float(x[2]), float(y[2]), float(z[2])
    tr = m00 + m11 + m22
    if tr > 0.0:
        s = math.sqrt(tr + 1.0) * 2.0
        w, qx, qy, qz = 0.25 * s, (m21 - m12) / s, (m02 - m20) / s, (m10 - m01) / s
    elif m00 > m11 and m00 > m22:
        s = math.sqrt(1.0 + m00 - m11 - m22) * 2.0
        w, qx, qy, qz = (m21 - m12) / s, 0.25 * s, (m01 + m10) / s, (m02 + m20) / s
    elif m11 > m22:
        s = math.sqrt(1.0 + m11 - m00 - m22) * 2.0
        w, qx, qy, qz = (m02 - m20) / s, (m01 + m10) / s, 0.25 * s, (m12 + m21) / s
    else:
        s = math.sqrt(1.0 + m22 - m00 - m11) * 2.0
        w, qx, qy, qz = (m10 - m01) / s, (m02 + m20) / s, (m12 + m21) / s, 0.25 * s
    return {"x": round(qx, 6), "y": round(qy, 6), "z": round(qz, 6), "w": round(w, 6)}


def look_rotation(forward, up):
    """Equivalent to Unity's Quaternion.LookRotation(forward, up)."""
    z = forward / np.linalg.norm(forward)
    x = np.cross(up, z)
    x = x / np.linalg.norm(x)
    y = np.cross(z, x)
    return quaternion_from_axes(x, y, z)


def vec_dict(v):
    return {"x": round(float(v[0]), 5), "y": round(float(v[1]), 5), "z": round(float(v[2]), 5)}


class TableFrame:
    """Transforms QTM global coordinates (Z-up, meters) to Unity table coordinates (Y-up, meters)."""

    def __init__(self, corner_left, corner_right, hips_pos):
        mid = 0.5 * (corner_left + corner_right)
        right = corner_right - corner_left
        right[2] = 0.0
        right = right / np.linalg.norm(right)
        forward = np.array([-right[1], right[0], 0.0], dtype=float)
        if np.dot(hips_pos - mid, forward) > 0.0:
            forward = -forward
            right = -right
        self.right = right
        self.forward = forward
        center = mid + TABLE_HALF_LENGTH * forward
        self.origin = np.array([center[0], center[1], 0.0], dtype=float)

    def point(self, p):
        d = p - self.origin
        return np.array([np.dot(d, self.right), p[2], np.dot(d, self.forward)], dtype=float)

    def direction(self, v):
        return np.array([np.dot(v, self.right), v[2], np.dot(v, self.forward)], dtype=float)


def build_marker_series(markers_list, name, start_frame, num_frames):
    """Return a list of length num_frames with 3D positions in meters (or None for gaps)."""
    entry = next((m for m in markers_list if m.get("Name") == name), None)
    if entry is None:
        raise KeyError(f"Marker '{name}' not found in QTM JSON")
    series = [None] * num_frames
    for part in entry.get("Parts", []):
        p_start = part["Range"]["Start"]
        for offset, val in enumerate(part["Values"]):
            idx = (p_start - start_frame) + offset
            if 0 <= idx < num_frames:
                series[idx] = np.array(val[:3], dtype=float) / 1000.0
    return series


def first_valid(series):
    for p in series:
        if p is not None:
            return p
    raise ValueError("Marker series has no valid samples")


def build_segment_series(segments_list, name, start_frame, num_frames):
    """Return (positions_m, quats_xyzw) arrays of length num_frames."""
    entry = next((s for s in segments_list if s.get("Name") == name), None)
    if entry is None:
        raise KeyError(f"Skeleton segment '{name}' not found in QTM JSON")
    pos = np.zeros((num_frames, 3), dtype=float)
    quat = np.zeros((num_frames, 4), dtype=float)
    quat[:, 3] = 1.0
    for part in entry.get("Parts", []):
        p_start = part["Range"]["Start"]
        for offset, val in enumerate(part["Values"]):
            idx = (p_start - start_frame) + offset
            if 0 <= idx < num_frames:
                pos[idx] = np.array(val[:3], dtype=float) / 1000.0
                quat[idx] = np.array(val[3:7], dtype=float)
    return pos, quat


def solve_rigid_transform(src_pts, dst_pts):
    """Kabsch algorithm: find R (3x3, det=+1) and t such that dst ≈ R @ src + t."""
    src_mean = src_pts.mean(axis=0)
    dst_mean = dst_pts.mean(axis=0)
    H = (src_pts - src_mean).T @ (dst_pts - dst_mean)
    U, _, Vt = np.linalg.svd(H)
    R = Vt.T @ U.T
    if np.linalg.det(R) < 0.0:
        Vt[-1, :] *= -1.0
        R = Vt.T @ U.T
    t = dst_mean - R @ src_mean
    return R, t


def compute_racket_poses(racket_series_list, wrist_in, wrist_out, left_handed):
    """Compute gap-free (center, tip, normal) in QTM global meters for every frame."""
    num_frames = len(racket_series_list[0])
    num_markers = len(racket_series_list)

    # Pick a reference frame where all racket markers are visible.
    ref_idx = next(
        (i for i in range(num_frames) if all(s[i] is not None for s in racket_series_list)),
        None,
    )
    if ref_idx is None:
        raise ValueError("No frame has all racket markers visible simultaneously")

    ref_pts = np.array([s[ref_idx] for s in racket_series_list], dtype=float)
    ref_center = ref_pts.mean(axis=0)

    # Plane normal from SVD of the rim ring points.
    _, _, vt = np.linalg.svd(ref_pts - ref_center)
    ref_normal = vt[-1]
    ref_normal /= np.linalg.norm(ref_normal)

    # Tip direction: from Racket1 (topmost/closest to thumb) toward the opposite rim (mean of middle/far markers),
    # projected onto the blade plane and verified to point away from the wrist.
    w_ref = 0.5 * (wrist_in[ref_idx] + wrist_out[ref_idx])
    far_idx = num_markers // 2
    ref_tip = ref_pts[far_idx] - ref_pts[0]
    ref_tip -= np.dot(ref_tip, ref_normal) * ref_normal
    ref_tip /= np.linalg.norm(ref_tip)
    if np.dot(ref_tip, ref_center - w_ref) < 0.0:
        ref_tip = -ref_tip

    # Orient forehand normal to match the palm direction from wrist markers.
    normal_votes = 0
    for i in range(0, num_frames, max(1, num_frames // 50)):
        visible = [m for m in range(num_markers) if racket_series_list[m][i] is not None]
        if len(visible) < 3:
            continue
        cur_pts = np.array([racket_series_list[m][i] for m in visible], dtype=float)
        R, t = solve_rigid_transform(ref_pts[visible], cur_pts)
        center_i = R @ ref_center + t
        tip_i = R @ ref_tip
        normal_i = R @ ref_normal
        w_in, w_out = wrist_in[i], wrist_out[i]
        if w_in is None or w_out is None or np.linalg.norm(center_i - 0.5 * (w_in + w_out)) > 0.6:
            continue
        thumb_up = (w_in - w_out)
        thumb_up /= np.linalg.norm(thumb_up)
        palm = np.cross(tip_i, thumb_up) if left_handed else np.cross(thumb_up, tip_i)
        normal_votes += 1 if np.dot(normal_i, palm) > 0.0 else -1
    if normal_votes < 0:
        ref_normal = -ref_normal

    centers = np.zeros((num_frames, 3), dtype=float)
    tips = np.zeros((num_frames, 3), dtype=float)
    normals = np.zeros((num_frames, 3), dtype=float)

    last_R = np.eye(3)
    last_t = np.zeros(3)
    for i in range(num_frames):
        visible = [m for m in range(num_markers) if racket_series_list[m][i] is not None]
        if len(visible) >= 3:
            cur_pts = np.array([racket_series_list[m][i] for m in visible], dtype=float)
            last_R, last_t = solve_rigid_transform(ref_pts[visible], cur_pts)
        centers[i] = last_R @ ref_center + last_t
        tips[i] = last_R @ ref_tip
        normals[i] = last_R @ ref_normal

    return centers, tips, normals


def detect_strokes(centers_table, spine2_table, sample_rate, min_speed, left_handed):
    """Detect strokes from racket speed peaks in table coordinates."""
    n = len(centers_table)
    dt = 1.0 / sample_rate
    speeds = np.zeros(n, dtype=float)
    for i in range(2, n - 2):
        speeds[i] = np.linalg.norm(centers_table[i + 2] - centers_table[i - 2]) / (4.0 * dt)

    gap = int(MIN_STROKE_GAP * sample_rate)
    strokes = []
    for i in range(gap, n - gap):
        if speeds[i] >= min_speed and speeds[i] == np.max(speeds[i - gap : i + gap + 1]):
            vel = (centers_table[i + 2] - centers_table[i - 2]) / (4.0 * dt)
            side_offset = (centers_table[i][0] - spine2_table[i][0]) * (-1.0 if left_handed else 1.0)
            forehand = side_offset > 0.15 and vel[2] > 0.0
            strokes.append({
                "t": i * dt,
                "speed": float(speeds[i]),
                "forehand": bool(forehand),
                "side_offset": float(side_offset),
            })
    return strokes


def convert_qtm_to_clip(args):
    with open(args.input, "r", encoding="utf-8") as f:
        data = json.load(f)

    tb = data["Timebase"]
    sample_rate = float(tb["Frequency"])
    start_frame = int(tb["Range"]["Start"])
    end_frame = int(tb["Range"]["End"])
    num_frames = end_frame - start_frame + 1
    duration = (num_frames - 1) / sample_rate

    markers = data.get("Markers", [])
    if not markers:
        raise ValueError(f"No 'Markers' found in {args.input}. Re-export from QTM with Markers enabled.")
    skeletons = data.get("Skeletons", [])
    if not skeletons:
        raise ValueError(f"No 'Skeletons' found in {args.input}.")
    segments = skeletons[0]["Segments"]

    # Table frame from TableL / TableR markers and Hips position.
    table_l = first_valid(build_marker_series(markers, args.table_markers[0], start_frame, num_frames))
    table_r = first_valid(build_marker_series(markers, args.table_markers[1], start_frame, num_frames))
    hips_pos, _ = build_segment_series(segments, "Hips", start_frame, num_frames)
    table = TableFrame(table_l, table_r, hips_pos[num_frames // 2])

    # Racket poses from rim markers + wrist orientation.
    side_prefix = "L" if args.left else "R"
    wrist_in = build_marker_series(markers, f"x_{side_prefix}WristIn", start_frame, num_frames)
    wrist_out = build_marker_series(markers, f"x_{side_prefix}WristOut", start_frame, num_frames)
    racket_series = [build_marker_series(markers, m, start_frame, num_frames) for m in args.racket_markers]
    racket_centers, racket_tips, racket_normals = compute_racket_poses(
        racket_series, wrist_in, wrist_out, args.left
    )

    # Body joints in table coordinates.
    joint_positions_table = {}
    head_quats = None
    for j_name in BODY_JOINTS:
        pos_qtm, quat_qtm = build_segment_series(segments, j_name, start_frame, num_frames)
        joint_positions_table[j_name] = np.array([table.point(p) for p in pos_qtm], dtype=float)
        if j_name == "Head":
            head_quats = quat_qtm

    racket_centers_table = np.array([table.point(p) for p in racket_centers], dtype=float)
    strokes = detect_strokes(
        racket_centers_table, joint_positions_table["Spine2"], sample_rate, args.min_speed, args.left
    )
    print(f"INFO {len(strokes)} strokes detected ({sum(s['forehand'] for s in strokes)} forehand, "
          f"{sum(not s['forehand'] for s in strokes)} backhand) over {duration:.2f} s")
    for s in strokes:
        kind = "forehand" if s["forehand"] else "backhand"
        print(f"INFO   t={s['t']:.2f}s  {kind:8s}  speed={s['speed']:.2f} m/s  side={s['side_offset']:+.2f} m")

    t_start = 0.0 if args.start is None else max(0.0, args.start)
    t_end = duration if args.end is None else min(duration, args.end)

    # Resample to OUTPUT_RATE (120 Hz).
    out_count = int(round((t_end - t_start) * OUTPUT_RATE))
    frames = []
    for i in range(out_count + 1):
        t_rel = i / OUTPUT_RATE
        t_abs = t_start + t_rel
        f_idx = t_abs * sample_rate
        i0 = max(0, min(num_frames - 2, int(math.floor(f_idx))))
        u = max(0.0, min(1.0, f_idx - i0))

        def lerp_arr(arr):
            return (1.0 - u) * arr[i0] + u * arr[i0 + 1]

        # Head rotation: in QTM skeleton, local Y (col 1) is forward, local Z (col 2) is up.
        R_head = quat_to_matrix(*head_quats[i0])
        head_face = table.direction(R_head[:, 1])
        head_up = table.direction(R_head[:, 2])
        head_pos = lerp_arr(joint_positions_table["Head"]) + 0.08 * (head_up / np.linalg.norm(head_up))

        # Paddle axes: y = handle to tip, z = away from forehand face, x = y cross z.
        tip_t = table.direction(lerp_arr(racket_tips))
        normal_t = table.direction(lerp_arr(racket_normals))
        y_ax = tip_t / np.linalg.norm(tip_t)
        z_ax = -normal_t - np.dot(-normal_t, y_ax) * y_ax
        z_ax /= np.linalg.norm(z_ax)
        x_ax = np.cross(y_ax, z_ax)

        frames.append({
            "t": round(t_rel, 5),
            "head_position": vec_dict(head_pos),
            "head_rotation": look_rotation(head_face, head_up),
            "paddle_position": vec_dict(lerp_arr(racket_centers_table)),
            "paddle_rotation": quaternion_from_axes(x_ax, y_ax, z_ax),
            "ball_in_play": False,
            "ball_position": {"x": 0.0, "y": 0.0, "z": 0.0},
            "robot_paddle_position": {"x": 0.0, "y": 0.0, "z": 0.0},
            "robot_paddle_rotation": {"x": 0.0, "y": 0.0, "z": 0.0, "w": 1.0},
            "joints": [vec_dict(lerp_arr(joint_positions_table[j])) for j in BODY_JOINTS],
        })

    events = [
        {"t": round(s["t"] - t_start, 4), "type": "contact" if s["forehand"] else "backhand"}
        for s in strokes
        if t_start <= s["t"] <= t_end
    ]
    clip_name = args.name or os.path.splitext(os.path.basename(args.output))[0]
    clip = {
        "name": clip_name,
        "source": "qualisys",
        "left_handed": args.left,
        "recorded": data.get("MeasurementCreation", ""),
        "has_ball": False,
        "has_opponent": False,
        "joint_names": BODY_JOINTS,
        "frames": frames,
        "events": events,
    }

    os.makedirs(os.path.dirname(os.path.abspath(args.output)), exist_ok=True)
    with open(args.output, "w", encoding="utf-8") as f:
        json.dump(clip, f, separators=(",", ":"))
    print(f"INFO wrote {args.output}: {len(frames)} frames, {len(events)} events, "
          f"{os.path.getsize(args.output) / 1e6:.2f} MB")


def parse_args():
    p = argparse.ArgumentParser(description="Convert QTM JSON mocap data to GhostCoach MotionClip JSON.")
    p.add_argument("input", help="Path to QTM JSON file")
    p.add_argument("output", help="Path to output MotionClip JSON file")
    p.add_argument("--name", default=None, help="Clip name (default: output file stem)")
    p.add_argument("--start", type=float, default=None, help="Start time in seconds")
    p.add_argument("--end", type=float, default=None, help="End time in seconds")
    p.add_argument("--left", action="store_true", help="Player is left-handed")
    p.add_argument("--min-speed", type=float, default=2.0, help="Min racket speed (m/s) for stroke detection")
    p.add_argument("--table-markers", nargs=2, default=list(DEFAULT_TABLE_MARKERS),
                   metavar=("LEFT", "RIGHT"), help="Left and right table corner marker names")
    p.add_argument("--racket-markers", nargs="+", default=list(DEFAULT_RACKET_MARKERS),
                   help="Racket rim marker names (starting with topmost/thumb-side)")
    return p.parse_args()


if __name__ == "__main__":
    convert_qtm_to_clip(parse_args())
