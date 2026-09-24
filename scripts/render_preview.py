import bpy
import os
from mathutils import Vector

root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=os.path.join(root, 'assets', 'chick_selected.glb'))
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
for track in arm.animation_data.nla_tracks:
    track.mute = True
arm.animation_data.action = bpy.data.actions['animation/anims/chicken/world/chick_idle01']
scene = bpy.context.scene
scene.render.engine = 'BLENDER_EEVEE'
scene.render.film_transparent = True
scene.render.resolution_x = 320
scene.render.resolution_y = 320
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = 'PNG'
scene.render.image_settings.color_mode = 'RGBA'
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
bpy.ops.object.camera_add()
camera = bpy.context.object
camera.data.type = 'ORTHO'
camera.data.ortho_scale = 0.5
scene.camera = camera
for name, location in [('front', (0, -0.75, 0.35)), ('back', (0, 0.75, 0.35)), ('side', (0.75, -0.3, 0.35))]:
    camera.location = location
    direction = Vector((0, 0, 0.18)) - camera.location
    camera.rotation_euler = direction.to_track_quat('-Z', 'Y').to_euler()
    scene.frame_set(12)
    scene.render.filepath = os.path.join(root, 'preview_' + name + '.png')
    bpy.ops.render.render(write_still=True)
