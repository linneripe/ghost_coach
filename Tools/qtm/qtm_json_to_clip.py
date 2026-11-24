#!/usr/bin/env python3
# Converts a QTM JSON export (File > Export > To JSON, with the skeleton) to a
# GhostCoach coach clip (the MotionClip JSON of Assets/Scripts/GhostCoach/
# MotionClip.cs).  No Blender needed.
#
#   python3 Tools/qtm/qtm_json_to_clip.py take.json \
#       Assets/Resources/GhostCoach/clips/backhand_drive.json --type backhand
#
# Options:
#   --name NAME         clip name (default: output file name)
#   --left              the player is left handed
#   --type TYPE         forehand or backhand: every stroke in the take is of
#                       this kind (default: decide from where the racket is)
#   --min-speed V       racket speed peak in m/s that counts as a stroke (2)
#   --start S --end E   seconds of the take to export (default: all of it)
#   --table-distance D  hips to the middle of the table, meters (1.9)
#   --table-center X Y  table centre on the floor in QTM coordinates, meters
#   --table-forward DX DY  direction from the player toward the opponent
#
# PROVISIONAL.  The QTM export only has the skeleton, so
#   - the racket is the hand plus an offset along the forearm, with its face
#     assumed to point toward the opponent (forehand) or away (backhand);
#     the racket angle is therefore NOT measured and the face angle part of
#     the score means nothing,
#   - the table is placed where the player's stance says (--table-distance in
#     front of the hips along the shoulder line's normal) unless the centre and
#     direction are given.
# With racket and table markers in the take, use the Blender route
# (Tools/blender) or extend this script to read them.
#
# QTM units are millimeters, z up.  Clips use table coordinates as in Unity:
# origin on the floor under the middle of the table, y up, z from the player
# toward the opponent, x to the player's right, meters.

import argparse
import json
import math
import os
import sys

OUTPUT_RATE = 120.0
MIN_STROKE_GAP = 0.5         # Seconds between strokes.
RACKET_FROM_HAND = 0.12      # Meters from the hand along the forearm to the racket centre.
HEAD_TOP_ABOVE_HEAD = 0.12   # Meters from the head joint to the top of the head.

JOINTS = ["Hips", "Spine2", "Neck", "Head", "HeadTop_End",
          "LeftArm", "LeftForeArm", "LeftHand", "RightArm", "RightForeArm", "RightHand",
          "LeftUpLeg", "LeftLeg", "LeftFoot", "LeftToeBase",
          "RightUpLeg", "RightLeg", "RightFoot", "RightToeBase"]


# ---- small vector helpers (tuples of 3 floats)

def add(a, b): return (a[0] + b[0], a[1] + b[1], a[2] + b[2])
def sub(a, b): return (a[0] - b[0], a[1] - b[1], a[2] - b[2])
def mul(a, s): return (a[0] * s, a[1] * s, a[2] * s)
def dot(a, b): return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]
def cross(a, b): return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])
def length(a): return math.sqrt(dot(a, a))
def unit(a):
    n = length(a)
    return (a[0] / n, a[1] / n, a[2] / n) if n > 1e-9 else (0.0, 0.0, 0.0)
def lerp(a, b, u): return add(mul(a, 1 - u), mul(b, u))


def quaternion_from_axes(x, y, z):
    # Rotation whose columns are the axes x, y, z, as {x, y, z, w}.
    m00, m01, m02 = x[0], y[0], z[0]
    m10, m11, m12 = x[1], y[1], z[1]
    m20, m21, m22 = x[2], y[2], z[2]
    tr = m00 + m11 + m22
    if tr > 0:
        s = math.sqrt(tr + 1.0) * 2
        w, qx, qy, qz = 0.25 * s, (m21 - m12) / s, (m02 - m20) / s, (m10 - m01) / s
    elif m00 > m11 and m00 > m22:
        s = math.sqrt(1.0 + m00 - m11 - m22) * 2
        w, qx, qy, qz = (m21 - m12) / s, 0.25 * s, (m01 + m10) / s, (m02 + m20) / s
    elif m11 > m22:
        s = math.sqrt(1.0 + m11 - m00 - m22) * 2
        w, qx, qy, qz = (m02 - m20) / s, (m01 + m10) / s, 0.25 * s, (m12 + m21) / s
    else:
        s = math.sqrt(1.0 + m22 - m00 - m11) * 2
        w, qx, qy, qz = (m10 - m01) / s, (m02 + m20) / s, (m12 + m21) / s, 0.25 * s
    return {"x": round(qx, 6), "y": round(qy, 6), "z": round(qz, 6), "w": round(w, 6)}


def look_rotation(forward, up):
    # Same as Unity's Quaternion.LookRotation: z along forward, y near up.
    z = unit(forward)
    x = unit(cross(up, z))
    y = cross(z, x)
    return quaternion_from_axes(x, y, z)


def vec(v):
    return {"x": round(v[0], 5), "y": round(v[1], 5), "z": round(v[2], 5)}


# ---- reading the QTM export

def read_segments(path):
    with open(path) as f:
        data = json.load(f)
    if "Skeletons" not in data or not data["Skeletons"]:
        sys.exit("No skeleton in %s: export from QTM with the skeleton included." % path)
    skeleton = data["Skeletons"][0]
    rate = float(skeleton["Timebase"]["Frequency"])
    positions = {}
    for seg in skeleton["Segments"]:
        values = seg["Parts"][0]["Values"]
        positions[seg["Name"]] = [(v[0] / 1000.0, v[1] / 1000.0, v[2] / 1000.0) for v in values]
    return positions, rate, data.get("Name", "")


def sampled(track, rate, t):
    # Position of a track (list of points at rate Hz) at time t.
    x = t * rate
    i = max(0, min(len(track) - 2, int(math.floor(x))))
    return lerp(track[i], track[i + 1], max(0.0, min(1.0, x - i)))


def main():
    p = argparse.ArgumentParser()
    p.add_argument("input")
    p.add_argument("output")
    p.add_argument("--name")
    p.add_argument("--left", action="store_true")
    p.add_argument("--type", choices=["forehand", "backhand"])
    p.add_argument("--min-speed", type=float, default=2.0)
    p.add_argument("--start", type=float)
    p.add_argument("--end", type=float)
    p.add_argument("--table-distance", type=float, default=1.9)
    p.add_argument("--table-center", type=float, nargs=2)
    p.add_argument("--table-forward", type=float, nargs=2)
    args = p.parse_args()

    seg, rate, qtm_name = read_segments(args.input)
    for name in JOINTS:
        if name != "HeadTop_End" and name not in seg:
            sys.exit("The skeleton has no joint named %s (it has %s ...)" % (name, ", ".join(list(seg)[:8])))
    frames_in = len(seg["Hips"])
    duration = (frames_in - 1) / rate
    start = args.start if args.start is not None else 0.0
    end = min(args.end if args.end is not None else duration, duration)
    side = "Left" if args.left else "Right"
    up = (0.0, 0.0, 1.0)

    def at(name, t):
        return sampled(seg[name], rate, t)

    def body_right(t):
        # Horizontal direction from the left to the right shoulder.
        r = sub(at("RightArm", t), at("LeftArm", t))
        return unit((r[0], r[1], 0.0))

    # Table frame: given, or from the player's stance.
    steps = int((end - start) * 10)
    hips_sum, forward_sum = (0.0, 0.0, 0.0), (0.0, 0.0, 0.0)
    for i in range(steps + 1):
        t = start + i * 0.1
        hips_sum = add(hips_sum, at("Hips", t))
        forward_sum = add(forward_sum, cross(up, body_right(t)))
    hips_mean = mul(hips_sum, 1.0 / (steps + 1))
    if args.table_forward:
        forward = unit((args.table_forward[0], args.table_forward[1], 0.0))
    else:
        forward = unit((forward_sum[0], forward_sum[1], 0.0))
    right = cross(forward, up)
    if args.table_center:
        origin = (args.table_center[0], args.table_center[1], 0.0)
    else:
        origin = (hips_mean[0] + forward[0] * args.table_distance,
                  hips_mean[1] + forward[1] * args.table_distance, 0.0)
    print("INFO table centre %.2f %.2f forward %.2f %.2f (%s)" % (
        origin[0], origin[1], forward[0], forward[1],
        "given" if args.table_center else "from the stance, provisional"))

    def point(w):
        d = sub(w, origin)
        return (dot(d, right), d[2], dot(d, forward))

    def direction(w):
        return (dot(w, right), w[2], dot(w, forward))

    # Racket centre: the hand plus an offset along the forearm.
    def racket(t):
        hand, fore = at(side + "Hand", t), at(side + "ForeArm", t)
        tip = unit(sub(hand, fore))
        return add(hand, mul(tip, RACKET_FROM_HAND)), tip

    # Strokes from racket speed peaks.
    dt = 0.01
    times = [start + i * dt for i in range(int((end - start) / dt) + 1)]
    centers = [racket(t)[0] for t in times]
    speeds = [0.0] + [length(sub(centers[i], centers[i - 1])) / dt for i in range(1, len(centers))]
    smooth = [sum(speeds[max(0, i - 2):i + 3]) / len(speeds[max(0, i - 2):i + 3]) for i in range(len(speeds))]
    strokes = []
    for i in range(2, len(smooth) - 2):
        if smooth[i] >= args.min_speed and smooth[i] == max(smooth[max(0, i - 25):i + 26]):
            if strokes and times[i] - strokes[-1]["t"] < MIN_STROKE_GAP:
                continue
            t = times[i]
            c = point(racket(t)[0])
            chest = point(at("Spine2", t))
            offset = (c[0] - chest[0]) * (-1.0 if args.left else 1.0)
            kind = args.type or ("forehand" if offset > 0.15 else "backhand")
            strokes.append({"t": t, "speed": smooth[i], "type": kind})
    for s in strokes:
        print("INFO stroke t %6.2f  %-8s speed %.1f m/s" % (s["t"], s["type"], s["speed"]))
    print("INFO %d strokes in %.1f s" % (len(strokes), end - start))

    def kind_at(t):
        if args.type or not strokes:
            return args.type or "forehand"
        return min(strokes, key=lambda s: abs(s["t"] - t))["type"]

    # Frames at the output rate.
    frames = []
    count = int((end - start) * OUTPUT_RATE)
    for i in range(count + 1):
        t = start + i / OUTPUT_RATE
        joints_w = {}
        for name in JOINTS:
            if name == "HeadTop_End":
                head, neck = at("Head", t), at("Neck", t)
                joints_w[name] = add(head, mul(unit(sub(head, neck)), HEAD_TOP_ABOVE_HEAD))
            else:
                joints_w[name] = at(name, t)
        joints = [point(joints_w[n]) for n in JOINTS]
        head_center = point(lerp(joints_w["Head"], joints_w["HeadTop_End"], 0.5))
        body_forward = direction(cross(up, body_right(t)))
        center_w, tip_w = racket(t)
        y = unit(direction(tip_w))
        # Forehand side of the blade faces the opponent in a forehand stroke
        # and away from the opponent in a backhand stroke (assumed).
        normal = (0.0, 0.0, 1.0 if kind_at(t) == "forehand" else -1.0)
        z = unit(sub(mul(normal, -1.0), mul(y, dot(mul(normal, -1.0), y))))
        x = cross(y, z)
        frames.append({
            "t": round(i / OUTPUT_RATE, 5),
            "head_position": vec(head_center),
            "head_rotation": look_rotation((body_forward[0], 0.0, body_forward[2]), (0.0, 1.0, 0.0)),
            "paddle_position": vec(point(center_w)),
            "paddle_rotation": quaternion_from_axes(x, y, z),
            "ball_in_play": False,
            "ball_position": vec((0.0, 0.0, 0.0)),
            "robot_paddle_position": vec((0.0, 0.0, 0.0)),
            "robot_paddle_rotation": {"x": 0, "y": 0, "z": 0, "w": 1},
            "joints": [vec(j) for j in joints],
        })

    events = [{"t": round(s["t"] - start, 4), "type": "contact" if s["type"] == "forehand" else "backhand"}
              for s in strokes]
    name = args.name or os.path.splitext(os.path.basename(args.output))[0]
    clip = {"name": name, "source": "qualisys", "left_handed": args.left, "recorded": "",
            "has_ball": False, "has_opponent": False, "joint_names": JOINTS,
            "frames": frames, "events": events}
    with open(args.output, "w") as f:
        json.dump(clip, f, separators=(",", ":"))
    print("INFO wrote %s: %d frames, %d strokes, %.1f MB"
          % (args.output, len(frames), len(events), os.path.getsize(args.output) / 1e6))


main()
