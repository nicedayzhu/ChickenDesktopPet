import bpy
import os
from mathutils import Vector

root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
bpy.ops.import_scene.gltf(filepath=os.path.join(root, 'assets', 'chick_selected.glb'))
print('OBJECTS', [(o.name, o.type, tuple(round(x, 3) for x in o.location)) for o in bpy.data.objects])
print('ACTIONS', [(a.name, tuple(a.frame_range)) for a in bpy.data.actions])
print('MATERIALS', [(m.name, m.use_nodes) for m in bpy.data.materials])
for obj in bpy.data.objects:
    if obj.type == 'MESH':
        corners = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
        print('BOUNDS', obj.name, [(round(min(v[i] for v in corners), 3), round(max(v[i] for v in corners), 3)) for i in range(3)])
