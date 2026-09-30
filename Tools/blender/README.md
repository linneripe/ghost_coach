# Blender tools

`export_coach_clip.py` turns a Qualisys take in Blender into a coach clip
for GhostCoach.  See the comment at the top of the script for options.

```
/Applications/Blender.app/Contents/MacOS/Blender -b take.blend \
    --python Tools/blender/export_coach_clip.py -- \
    Assets/Resources/GhostCoach/clips/coach_take0006.json --min-speed 2
```

It writes two files next to each other in `Assets/Resources/GhostCoach/clips/`:

| File | What | In git? |
|---|---|---|
| `NAME.json` | The coach's motion: racket, head, body joints and the bone poses of the exported window. Numbers only, no video or names. | Yes, with the consent of the person recorded |
| `NAME.body.bytes` | The character mesh with skin weights (the Mixamo mannequin of the rig). | **No.** Mixamo characters may not be redistributed as standalone files. Share it in LU Box and copy it into the same folder. |

Without the `.body.bytes` file the app still works: the coach is then drawn
as a simple figure of capsules instead of the character.  The automated
tests skip the checks that need the mesh when the file is missing.
