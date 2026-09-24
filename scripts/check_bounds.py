import bpy, os
root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=os.path.join(root, 'assets', 'chick_selected.glb'))
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
model = next(o for o in bpy.data.objects if o.type == 'MESH' and o.name.endswith('.chicken'))
for track in arm.animation_data.nla_tracks: track.mute = True
for name,frames in [('run',[0,4,8,14]),('trick02',[0,13,20,30,39])]:
    act = next(a for a in bpy.data.actions if a.name.endswith('/chick_'+name))
    arm.animation_data.action = act
    for f in frames:
        position = f / 12 * bpy.context.scene.render.fps
        bpy.context.scene.frame_set(int(position), subframe=position%1)
        ob = model.evaluated_get(bpy.context.evaluated_depsgraph_get())
        mesh = ob.to_mesh()
        v = mesh.vertices
        print(name, f, 'X', min(x.co.x for x in v),max(x.co.x for x in v),'Y',min(x.co.y for x in v),max(x.co.y for x in v),'Z',min(x.co.z for x in v),max(x.co.z for x in v),'visible',model.hide_render)
        ob.to_mesh_clear()
