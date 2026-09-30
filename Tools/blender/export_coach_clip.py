# Converts a Qualisys take in a Blender file to a GhostCoach coach clip
# (the MotionClip JSON format of Assets/Scripts/GhostCoach/MotionClip.cs).
#
#   /Applications/Blender.app/Contents/MacOS/Blender -b take.blend \
#       --python Tools/blender/export_coach_clip.py -- out.json [options]
#
# Options after "--":
#   out.json            where to write the clip ("-" only lists the strokes)
#   --name NAME         clip name (default: output file name)
#   --start S --end E   seconds of the take to export (default: the 10 s
#                       with the most forehand strokes)
#   --left              the player is left handed
#   --min-speed V       racket speed peak in m/s that counts as a stroke
#                       (default 4; lower for slow play)
#   --no-body           do not export the character mesh and bone motion
#
# Unless --no-body is given the character mesh of the rig (the objects
# parented to "Armature", e.g. Alpha_Surface and Alpha_Joints) is written
# next to the clip as NAME.body.bytes, and the bone poses of the exported
# window are stored in the clip, so Unity can draw the same figure as
# Blender does.  The two files belong together: they use the same table
# frame.  Mesh format (little endian): magic "GCB1", bone count, vertex
# count, submesh count, index count per submesh, bone names, rest
# positions of the bones (3 floats each), vertices (3 floats), normals (3
# floats), bone indices (4 bytes), bone weights (4 floats), triangle
# indices (ints).  Everything in table coordinates in the rest pose.
#
# The Blender file is expected to have, as in the lab's bake.blend:
#   - "Armature": Mixamo rig with the body motion baked at the scene rate
#   - "racket:racket0..2": racket markers, keyed at 200 Hz
#   - "bord0", "bord1": markers on the two corners of the player's end of
#     the table
#   - "x_RWristIn", "x_RWristOut" (or L for left handed): wrist markers
#
# Coordinates: Blender is right handed with z up, in meters.  Clips use
# table coordinates as in Unity: origin on the floor under the middle of
# the table, y up, z from the player toward the opponent, x to the
# player's right.  The mocap has no ball, so strokes are found from racket
# speed peaks and the clip has no ball or opponent.

import bpy, json, math, sys, os, struct, base64
from array import array
from mathutils import Vector, Matrix
from bpy_extras import anim_utils

RACKET_RATE = 200.0          # The racket markers are keyed once per 200 Hz sample.
OUTPUT_RATE = 120.0
TABLE_HALF_LENGTH = 1.37
MIN_STROKE_SPEED = 4.0       # m/s racket speed peak that counts as a stroke.
MIN_STROKE_GAP = 0.5         # Seconds between strokes.

JOINTS = ["Hips", "Spine2", "Neck", "Head", "HeadTop_End",
          "LeftArm", "LeftForeArm", "LeftHand", "RightArm", "RightForeArm", "RightHand",
          "LeftUpLeg", "LeftLeg", "LeftFoot", "LeftToeBase",
          "RightUpLeg", "RightLeg", "RightFoot", "RightToeBase"]


def arguments():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    a = {"out": "-", "name": None, "start": None, "end": None, "left": False, "min-speed": MIN_STROKE_SPEED,
         "body": True}
    i = 0
    while i < len(argv):
        if argv[i] == "--left":
            a["left"] = True
        elif argv[i] == "--no-body":
            a["body"] = False
        elif argv[i] in ("--name", "--start", "--end", "--min-speed"):
            a[argv[i][2:]] = argv[i + 1]
            i += 1
        else:
            a["out"] = argv[i]
        i += 1
    for k in ("start", "end", "min-speed"):
        if a[k] is not None:
            a[k] = float(a[k])
    return a


def include_all_objects():
    # Markers can be in collections excluded from the view layer, which are
    # not evaluated.  Include them in memory; the file is not saved.
    scene, layer = bpy.context.scene, bpy.context.view_layer
    def unexclude(lc):
        lc.exclude = False
        for child in lc.children:
            unexclude(child)
    unexclude(layer.layer_collection)
    present = set(o.name for o in layer.objects)
    for o in bpy.data.objects:
        if o.name not in present:
            try:
                scene.collection.objects.link(o)
            except RuntimeError:
                pass
    layer.update()


def location_curves(obj):
    ad = obj.animation_data
    bag = anim_utils.action_get_channelbag_for_slot(ad.action, ad.action_slot)
    curves = [None, None, None]
    for fc in bag.fcurves:
        if fc.data_path == "location":
            curves[fc.array_index] = fc
    return curves


def marker_position(obj, curves, frame):
    # World position of an animated marker at a (fractional) frame.
    local = Vector((curves[0].evaluate(frame), curves[1].evaluate(frame), curves[2].evaluate(frame)))
    return obj.parent.matrix_world @ local if obj.parent else local


class TableFrame:
    # Table coordinates from the two markers on the player's end corners.
    def __init__(self, corner0, corner1, player_position):
        mid = 0.5 * (corner0 + corner1)
        along = (corner1 - corner0)
        along.z = 0.0
        along.normalize()
        toward_table = Vector((-along.y, along.x, 0.0))
        if (player_position - mid).dot(toward_table) > 0:     # Player is on the other side.
            toward_table = -toward_table
        self.forward = toward_table                           # Unity z.
        self.right = toward_table.cross(Vector((0, 0, 1)))    # Unity x.
        center = mid + TABLE_HALF_LENGTH * toward_table
        self.origin = Vector((center.x, center.y, 0.0))
        # Rows are the table axes, so basis @ v gives table coordinates.
        # It mirrors (determinant -1), which flips triangle winding.
        self.basis = Matrix((self.right, Vector((0, 0, 1)), self.forward))

    def rotation(self, r):
        # A rotation matrix in world coordinates as one in table coordinates.
        return self.basis @ r @ self.basis.transposed()

    def point(self, p):
        d = p - self.origin
        return Vector((d.dot(self.right), p.z, d.dot(self.forward)))

    def direction(self, v):
        return Vector((v.dot(self.right), v.z, v.dot(self.forward)))


def quaternion_from_axes(x, y, z):
    # Rotation whose local axes are x, y, z (orthonormal, x = y cross z).
    m00, m01, m02 = x.x, y.x, z.x
    m10, m11, m12 = x.y, y.y, z.y
    m20, m21, m22 = x.z, y.z, z.z
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
    return {"x": qx, "y": qy, "z": qz, "w": w}


def look_rotation(forward, up):
    # Same as Unity's Quaternion.LookRotation: z along forward, y near up.
    z = forward.normalized()
    x = up.cross(z).normalized()
    y = z.cross(x)
    return quaternion_from_axes(x, y, z)


def vec(v):
    return {"x": round(v.x, 5), "y": round(v.y, 5), "z": round(v.z, 5)}


BONE_PREFIX_SKIP = ()


def matrix_quaternion(m):
    return quaternion_from_axes(Vector(m.col[0]), Vector(m.col[1]), Vector(m.col[2]))


def rotation_of(matrix):
    return matrix.to_3x3().normalized()


def write_body(path, arm, table, rest_frame):
    # Character mesh in the rest pose, skin weights and rest bone positions
    # in table coordinates.  The rest pose is the armature at rest_frame.
    scene = bpy.context.scene
    scene.frame_set(rest_frame)
    dg = bpy.context.evaluated_depsgraph_get()
    ae = arm.evaluated_get(dg)
    world = ae.matrix_world
    bones = list(arm.data.bones)
    index = {b.name: i for i, b in enumerate(bones)}
    meshes = [o for o in arm.children if o.type == "MESH"]
    meshes.sort(key=lambda o: (0 if "Surface" in o.name else 1, o.name))
    positions, normals, bone_index, weights, submeshes = [], [], [], [], []
    for obj in meshes:
        mesh = obj.data
        mw = obj.evaluated_get(dg).matrix_world
        rot = rotation_of(mw)
        names = [g.name for g in obj.vertex_groups]
        base = len(positions)
        for v in mesh.vertices:
            positions.append(table.point(mw @ v.co))
            normals.append(table.direction(rot @ v.normal).normalized())
            groups = sorted(((g.weight, names[g.group]) for g in v.groups if names[g.group] in index), reverse=True)[:4]
            total = sum(w for w, _ in groups)
            if total <= 1e-6:
                groups, total = [(1.0, bones[0].name)], 1.0
            ids = [index[n] for _, n in groups] + [0] * (4 - len(groups))
            ws = [w / total for w, _ in groups] + [0.0] * (4 - len(groups))
            bone_index.append(ids)
            weights.append(ws)
        mesh.calc_loop_triangles()
        tris = []
        for t in mesh.loop_triangles:
            a, b, c = t.vertices
            tris += [base + a, base + c, base + b]           # Flipped: the table basis mirrors.
        submeshes.append(tris)
        print("INFO body mesh", obj.name, len(mesh.vertices), "vertices", len(tris) // 3, "triangles")
    rest = [table.point(world @ b.head_local) for b in bones]
    with open(path, "wb") as f:
        f.write(b"GCB1")
        f.write(struct.pack("<iiii", len(bones), len(positions), len(submeshes), 0))
        for tris in submeshes:
            f.write(struct.pack("<i", len(tris)))
        for b in bones:
            name = b.name.encode("utf-8")
            f.write(struct.pack("<i", len(name)))
            f.write(name)
        for p in rest:
            f.write(struct.pack("<3f", p.x, p.y, p.z))
        for p in positions:
            f.write(struct.pack("<3f", p.x, p.y, p.z))
        for n in normals:
            f.write(struct.pack("<3f", n.x, n.y, n.z))
        for ids in bone_index:
            f.write(struct.pack("<4B", *ids))
        for ws in weights:
            f.write(struct.pack("<4f", *ws))
        for tris in submeshes:
            f.write(array("i", tris).tobytes())
    print("INFO wrote body mesh %s: %d bones, %d vertices, %.1f MB"
          % (path, len(bones), len(positions), os.path.getsize(path) / 1e6))
    skin = {"meshes": meshes, "positions": positions, "bone_index": bone_index, "weights": weights, "rest": rest}
    return [b.name for b in bones], [Matrix(rotation_of(world @ b.matrix_local)) for b in bones], skin


def verify_skin(arm, table, skin, bone_floats, bone_count, first, k):
    # Rebuild the deformed mesh at exported frame k the way Unity does (linear
    # blend skinning from the exported bone poses) and compare it with what
    # Blender itself computes.  A wrong rotation convention or mirroring
    # shows up here as errors of many centimeters.
    from mathutils import Quaternion
    scene = bpy.context.scene
    scene.frame_set(scene.frame_start + first + k)
    dg = bpy.context.evaluated_depsgraph_get()
    stride = 7 * bone_count
    poses = []
    for i in range(bone_count):
        o = k * stride + 7 * i
        pos = Vector(bone_floats[o:o + 3])
        q = Quaternion((bone_floats[o + 6], bone_floats[o + 3], bone_floats[o + 4], bone_floats[o + 5]))
        poses.append((pos, q))
    errors, moved = [], []
    n = 0
    for obj in skin["meshes"]:
        ev = obj.evaluated_get(dg)
        mesh = ev.to_mesh()
        for v in mesh.vertices:
            target = table.point(ev.matrix_world @ v.co)
            rest_v = skin["positions"][n]
            got = Vector((0, 0, 0))
            for b, w in zip(skin["bone_index"][n], skin["weights"][n]):
                if w > 0:
                    pos, q = poses[b]
                    got += w * (q @ (rest_v - skin["rest"][b]) + pos)
            errors.append((got - target).length)
            moved.append((target - rest_v).length)
            n += 1
        ev.to_mesh_clear()
    errors.sort()
    print("INFO skin check frame %d: mean %.4f m, 95%% %.4f m, max %.4f m over %d vertices, "
          "the figure has moved %.2f m on average from the rest pose"
          % (k, sum(errors) / len(errors), errors[int(0.95 * len(errors))], errors[-1], len(errors),
             sum(moved) / len(moved)))
    return errors[int(0.95 * len(errors))]


def main():
    args = arguments()
    include_all_objects()
    scene = bpy.context.scene
    body_rate = scene.render.fps / scene.render.fps_base
    side = "L" if args["left"] else "R"

    arm = bpy.data.objects["Armature"]
    markers = [bpy.data.objects["racket:racket%d" % i] for i in range(3)]
    marker_curves = [location_curves(m) for m in markers]
    wrist_in, wrist_out = bpy.data.objects["x_%sWristIn" % side], bpy.data.objects["x_%sWristOut" % side]
    wrist_in_curves, wrist_out_curves = location_curves(wrist_in), location_curves(wrist_out)
    duration = (scene.frame_end - scene.frame_start) / body_rate

    def racket_markers(t):
        f = 1 + t * RACKET_RATE
        return [marker_position(m, c, f) for m, c in zip(markers, marker_curves)]

    def wrist(t):
        f = 1 + t * body_rate
        return (marker_position(wrist_in, wrist_in_curves, f), marker_position(wrist_out, wrist_out_curves, f))

    # Which marker is where on the racket.  Two markers are on the sides of
    # the blade (the pair furthest apart) and one on its center line; the
    # blade center is taken as the middle of the side pair.
    p = racket_markers(0.0)
    pairs = sorted([((p[i] - p[j]).length, i, j) for i in range(3) for j in range(i + 1, 3)], reverse=True)
    _, side_a, side_b = pairs[0]
    mid_marker = 3 - side_a - side_b
    print("INFO racket marker distances (m):", ["%.3f" % d for d, _, _ in pairs],
          "side markers", side_a, side_b, "center line marker", mid_marker)

    def racket_pose(t, tip_sign, normal_sign):
        m = racket_markers(t)
        center = 0.5 * (m[side_a] + m[side_b])
        tip = (m[mid_marker] - center).normalized() * tip_sign
        across = (m[side_b] - m[side_a]).normalized()
        normal = across.cross(tip).normalized() * normal_sign
        return center, tip, normal

    # Signs: the blade tip points away from the wrist, and for a shakehand
    # grip the forehand side faces the way the palm does.  Vote over the take.
    tip_votes = normal_votes = 0
    steps = int(duration * 10)
    for i in range(steps):
        t = i * 0.1
        center, tip, normal = racket_pose(t, 1.0, 1.0)
        w_in, w_out = wrist(t)
        w = 0.5 * (w_in + w_out)
        if (center - w).length > 0.6:       # Racket not in the hand (lying on the table).
            continue
        tip_votes += 1 if tip.dot(center - w) > 0 else -1
    tip_sign = 1.0 if tip_votes >= 0 else -1.0
    for i in range(steps):
        t = i * 0.1
        center, tip, normal = racket_pose(t, tip_sign, 1.0)
        w_in, w_out = wrist(t)
        if (center - 0.5 * (w_in + w_out)).length > 0.6:
            continue
        thumb_up = (w_in - w_out).normalized()
        palm = thumb_up.cross(tip) if not args["left"] else tip.cross(thumb_up)
        normal_votes += 1 if normal.dot(palm) > 0 else -1
    normal_sign = 1.0 if normal_votes >= 0 else -1.0
    print("INFO racket tip votes", tip_votes, "forehand normal votes", normal_votes, "of", steps)

    # Table frame from the corner markers (static) and where the player stands.
    scene.frame_set(scene.frame_start + int(0.5 * duration * body_rate))
    dg = bpy.context.evaluated_depsgraph_get()
    corner0 = bpy.data.objects["bord0"].evaluated_get(dg).matrix_world.translation.copy()
    corner1 = bpy.data.objects["bord1"].evaluated_get(dg).matrix_world.translation.copy()
    arm_eval = arm.evaluated_get(dg)
    hips = arm_eval.matrix_world @ arm_eval.pose.bones["mixamorig:Hips"].head
    table = TableFrame(corner0, corner1, hips)
    print("INFO table corners", tuple(round(c, 3) for c in corner0), tuple(round(c, 3) for c in corner1),
          "width %.3f m, marker height %.3f m" % ((corner1 - corner0).length, corner0.z))

    # Strokes: racket speed peaks.  Forehand when the racket is on the
    # playing hand side of the body and moving toward the opponent.
    dt = 1.0 / RACKET_RATE
    n = int(duration * RACKET_RATE)
    centers = [table.point(racket_pose(i * dt, tip_sign, normal_sign)[0]) for i in range(n)]
    speeds = [0.0] * n
    for i in range(2, n - 2):
        speeds[i] = (centers[i + 2] - centers[i - 2]).length / (4 * dt)
    strokes = []
    gap = int(MIN_STROKE_GAP * RACKET_RATE)
    for i in range(gap, n - gap):
        if speeds[i] >= args["min-speed"] and speeds[i] == max(speeds[i - gap:i + gap + 1]):
            t = i * dt
            w_in, w_out = wrist(t)
            if (racket_pose(t, tip_sign, normal_sign)[0] - 0.5 * (w_in + w_out)).length > 0.6:
                continue
            scene.frame_set(scene.frame_start + int(round(t * body_rate)))
            dg = bpy.context.evaluated_depsgraph_get()
            ae = arm.evaluated_get(dg)
            chest = table.point(ae.matrix_world @ ae.pose.bones["mixamorig:Spine2"].head)
            velocity = (centers[i + 2] - centers[i - 2]) / (4 * dt)
            offset = (centers[i].x - chest.x) * (-1.0 if args["left"] else 1.0)
            # Clearly out on the playing hand side; strokes in front of the
            # body are ambiguous and are left out as backhand.
            forehand = offset > 0.15 and velocity.z > 0.0
            strokes.append({"t": t, "speed": speeds[i], "forehand": forehand, "side_offset": offset,
                            "height": centers[i].y, "z": centers[i].z})
    print("INFO %d strokes, %d forehand, take %.1f s" % (len(strokes), sum(s["forehand"] for s in strokes), duration))
    for s in strokes:
        print("INFO stroke t %6.2f  %s  speed %4.1f m/s  side %+.2f m  height %.2f  z %+.2f"
              % (s["t"], "forehand" if s["forehand"] else "backhand", s["speed"], s["side_offset"], s["height"], s["z"]))

    if args["out"] == "-":
        return

    # Window to export: given, or the 10 s with the most forehand strokes,
    # starting a little before a forehand so it is the reference stroke.
    start, end = args["start"], args["end"]
    if start is None:
        best = (-1, 0.0)
        for s in strokes:
            if not s["forehand"]:
                continue
            w0 = max(0.0, s["t"] - 1.5)
            count = sum(1 for q in strokes if q["forehand"] and w0 <= q["t"] <= w0 + 10.0 - 1.0)
            if count > best[0]:
                best = (count, w0)
        start = best[1]
        end = min(duration, start + 10.0)
    print("INFO exporting %.2f - %.2f s" % (start, end))

    # Body joints at the body rate, then everything resampled to the output rate.
    first = int(math.floor(start * body_rate))
    last = int(math.ceil(end * body_rate)) + 1
    body = []
    bone_names, rest_rotations, skin = [], [], None
    bone_floats = array("f")
    if args["body"]:
        body_path = os.path.splitext(args["out"])[0] + ".body.bytes"
        bone_names, rest_rotations, skin = write_body(body_path, arm, table, scene.frame_start)
    for f in range(first, last + 1):
        scene.frame_set(scene.frame_start + f)
        dg = bpy.context.evaluated_depsgraph_get()
        ae = arm.evaluated_get(dg)
        for i, name in enumerate(bone_names):
            pb = ae.pose.bones[name]
            pos = table.point(ae.matrix_world @ pb.head)
            # World rotation change from the rest pose, as a table rotation.
            delta = table.rotation(rotation_of(ae.matrix_world @ pb.matrix) @ rest_rotations[i].inverted())
            q = matrix_quaternion(delta)
            bone_floats.extend([pos.x, pos.y, pos.z, q["x"], q["y"], q["z"], q["w"]])
        joints = [table.point(ae.matrix_world @ ae.pose.bones["mixamorig:" + j].head) for j in JOINTS]
        head = ae.pose.bones["mixamorig:Head"]
        world = ae.matrix_world @ head.matrix
        up = table.direction(world.col[1].xyz.normalized())
        face = table.direction(world.col[2].xyz.normalized())
        body.append((joints, up, face))

    if skin is not None:
        frames_exported = last - first + 1
        worst = max(verify_skin(arm, table, skin, bone_floats, len(bone_names), first, k)
                    for k in (0, frames_exported // 2, frames_exported - 1))
        if worst > 0.03:
            print("INFO WARNING skin does not match Blender, 95%% of vertices within %.3f m" % worst)

    def body_at(t):
        x = t * body_rate - first
        i = max(0, min(len(body) - 2, int(math.floor(x))))
        u = max(0.0, min(1.0, x - i))
        a, b = body[i], body[i + 1]
        joints = [ja.lerp(jb, u) for ja, jb in zip(a[0], b[0])]
        return joints, a[1].lerp(b[1], u), a[2].lerp(b[2], u)

    frames = []
    count = int((end - start) * OUTPUT_RATE)
    head_index, top_index = JOINTS.index("Head"), JOINTS.index("HeadTop_End")
    for i in range(count + 1):
        t = start + i / OUTPUT_RATE
        center, tip, normal = racket_pose(t, tip_sign, normal_sign)
        joints, head_up, head_face = body_at(t)
        tip_t, normal_t = table.direction(tip), table.direction(normal)
        # Paddle axes: y handle to tip, z away from the forehand side.
        y = tip_t.normalized()
        z = (-normal_t - (-normal_t).dot(y) * y).normalized()
        x = y.cross(z)
        frames.append({
            "t": round(i / OUTPUT_RATE, 5),
            "head_position": vec(0.5 * (joints[head_index] + joints[top_index])),
            "head_rotation": look_rotation(head_face, head_up),
            "paddle_position": vec(table.point(center)),
            "paddle_rotation": quaternion_from_axes(x, y, z),
            "ball_in_play": False,
            "ball_position": vec(Vector((0, 0, 0))),
            "robot_paddle_position": vec(Vector((0, 0, 0))),
            "robot_paddle_rotation": {"x": 0, "y": 0, "z": 0, "w": 1},
            "joints": [vec(j) for j in joints],
        })
    events = [{"t": round(s["t"] - start, 4), "type": "contact" if s["forehand"] else "backhand"}
              for s in strokes if start <= s["t"] <= end]
    name = args["name"] or os.path.splitext(os.path.basename(args["out"]))[0]
    clip = {"name": name, "source": "qualisys", "left_handed": args["left"], "recorded": "",
            "body": (os.path.basename(os.path.splitext(args["out"])[0]) + ".body") if bone_names else "",
            "bone_names": bone_names, "bone_rate": body_rate, "bone_t0": first / body_rate - start,
            "bone_frames": (last - first + 1) if bone_names else 0,
            "bone_data": base64.b64encode(bone_floats.tobytes()).decode("ascii") if bone_names else "",
            "has_ball": False, "has_opponent": False, "joint_names": JOINTS,
            "frames": frames, "events": events}
    with open(args["out"], "w") as f:
        json.dump(clip, f, separators=(",", ":"))
    print("INFO wrote %s: %d frames, %d forehand and %d backhand strokes, %.1f MB"
          % (args["out"], len(frames), sum(e["type"] == "contact" for e in events),
             sum(e["type"] == "backhand" for e in events), os.path.getsize(args["out"]) / 1e6))


main()
