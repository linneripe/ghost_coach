# QTM tools

`qtm_json_to_clip.py` turns a QTM JSON export of a take (the skeleton) into a
coach clip for GhostCoach, without Blender.  See the comment at the top of the
script for options.

```
python3 Tools/qtm/qtm_json_to_clip.py pro-mocap-data/backhand_drive0008.json \
    Assets/Resources/GhostCoach/clips/backhand_drive.json --type backhand
```

The QTM file needs the skeleton (File > Export > To JSON). The clip has the
body joints, the head, a racket and the strokes, all in table coordinates.

**Provisional.** A skeleton-only export has no racket and no table:
- the racket is the hand plus an offset along the forearm, and its face angle
  is assumed, so the face angle part of the score is not meaningful;
- the table is placed in front of the player's stance (`--table-distance`),
  or where `--table-center` and `--table-forward` say.

With the racket (3 markers) and the table corners (2 markers) in the take, use
the Blender route in `Tools/blender`, or extend this script to read them.
