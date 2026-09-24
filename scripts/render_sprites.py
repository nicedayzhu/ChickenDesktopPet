"""Bake selected CS2 chick actions to transparent PNGs with Steam Blender.

Usage: blender -b -t 4 --python scripts/render_sprites.py
"""
import bpy
import json
import math
import os
from mathutils import Vector

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, 'Pet', 'Assets', 'Sprites')
os.makedirs(OUT, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=os.path.join(ROOT, 'assets', 'chick_selected.glb'))
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
model = next(o for o in bpy.data.objects if o.type == 'MESH' and o.name.endswith('.chicken'))
for track in arm.animation_data.nla_tracks:
    track.mute = True

scene = bpy.context.scene
scene.render.engine = 'BLENDER_EEVEE'
scene.render.film_transparent = True
scene.render.resolution_x = 256
scene.render.resolution_y = 256
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = 'PNG'
scene.render.image_settings.color_mode = 'RGBA'
scene.render.image_settings.compression = 35
scene.view_settings.view_transform = 'AgX'
scene.world = bpy.data.worlds.new('World')
scene.world.use_nodes = True
scene.world.node_tree.nodes['Background'].inputs['Color'].default_value = (0.8, 0.8, 0.8, 1)
scene.world.node_tree.nodes['Background'].inputs['Strength'].default_value = 0.35

for mat in bpy.data.materials:
    if not mat.name.startswith('chick_'):
        continue
    bsdf = next(n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    for socket in ('Metallic', 'Roughness', 'Normal'):
        for link in list(bsdf.inputs[socket].links):
            mat.node_tree.links.remove(link)
    bsdf.inputs['Metallic'].default_value = 0
    bsdf.inputs['Roughness'].default_value = 0.82

for loc, energy, size in [((0, -1, 1), 100, 1), ((0.5, 0.5, 0.8), 65, 1), ((-0.5, 0.2, 0.6), 40, 0.8)]:
    bpy.ops.object.light_add(type='AREA', location=loc)
    lamp = bpy.context.object
    lamp.data.energy = energy
    lamp.data.shape = 'DISK'
    lamp.data.size = size

bpy.ops.object.camera_add(location=(0.65, -0.75, 0.38))
camera = bpy.context.object
camera.data.type = 'ORTHO'
camera.data.ortho_scale = 0.52
camera.rotation_euler = (Vector((0, 0, 0.18)) - camera.location).to_track_quat('-Z', 'Y').to_euler()
scene.camera = camera
camera_base = camera.location.copy()

actions = {
    'idle': ('animation/anims/chicken/world/chick_idle01', 24, None),
    'idle2': ('animation/anims/chicken/world/chick_idle02', 24, None),
    'walk': ('animation/anims/chicken/world/chick_walk', 24, None),
    'run': ('animation/anims/chicken/world/chick_run', 24, None),
    'react': ('animation/anims/chicken/world/chick_react01', 24, None),
    'react2': ('animation/anims/chicken/world/chick_react02', 24, None),
    'trick': ('animation/anims/chicken/world/chick_trick01', 24, None),
    'trick2': ('animation/anims/chicken/world/chick_trick02', 24, None),
    'sleep': ('animation/anims/chicken/world/chick_sleep_loop01', 24, None),
    'feed': ('animation/anims/chicken/ui/chickbaby_feed02', 24, 8),
}

only = set(filter(None, os.environ.get('CHICK_CLIPS', '').split(',')))
manifest_path = os.path.join(OUT, 'manifest.json')
if only and os.path.exists(manifest_path):
    with open(manifest_path, encoding='utf8') as stream:
        manifest = json.load(stream)
else:
    manifest = {}
for key, (action_name, fps, max_seconds) in actions.items():
    if only and key not in only:
        continue
    action = bpy.data.actions[action_name]
    arm.animation_data.action = action
    duration = (action.frame_range[1] - action.frame_range[0]) / scene.render.fps
    if max_seconds:
        duration = min(duration, max_seconds)
    count = max(2, math.ceil(duration * fps))
    folder = os.path.join(OUT, key)
    os.makedirs(folder, exist_ok=True)
    for i in range(count):
        position = action.frame_range[0] + (i / fps) * scene.render.fps
        whole = math.floor(position)
        scene.frame_set(whole, subframe=position - whole)
        evaluated = model.evaluated_get(bpy.context.evaluated_depsgraph_get())
        evaluated_mesh = evaluated.to_mesh()
        x_values = [vertex.co.x for vertex in evaluated_mesh.vertices]
        y_values = [vertex.co.y for vertex in evaluated_mesh.vertices]
        z_values = [vertex.co.z for vertex in evaluated_mesh.vertices]
        evaluated.to_mesh_clear()
        center_x = (min(x_values) + max(x_values)) / 2
        center_y = (min(y_values) + max(y_values)) / 2
        center_z = (min(z_values) + max(z_values)) / 2
        camera.location = camera_base + Vector((center_x, center_y, center_z - 0.18))
        scene.render.filepath = os.path.join(folder, f'{i:03}.png')
        bpy.ops.render.render(write_still=True)
    manifest[key] = {'fps': fps, 'frames': count, 'loop': key in ('idle', 'walk', 'run', 'sleep')}
    print('BAKED', key, count, flush=True)

with open(os.path.join(OUT, 'manifest.json'), 'w', encoding='utf8') as stream:
    json.dump(manifest, stream, indent=2)
