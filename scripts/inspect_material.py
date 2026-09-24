exec(open(__file__.replace('inspect_material.py','inspect_model.py'), encoding='utf8').read().split("print('OBJECTS'")[0])
for m in bpy.data.materials:
    print('MATERIAL', m.name)
    for n in m.node_tree.nodes:
        if n.type == 'BSDF_PRINCIPLED':
            print('BASE', tuple(n.inputs['Base Color'].default_value), 'METALLIC', n.inputs['Metallic'].default_value, 'ROUGHNESS', n.inputs['Roughness'].default_value)
        elif n.type == 'TEX_IMAGE':
            print('TEXTURE', n.name, n.image.name if n.image else None)
        elif n.type == 'VERTEX_COLOR' or n.type == 'ATTRIBUTE':
            print('COLOR ATTRIBUTE', n.name, getattr(n, 'layer_name', None), getattr(n, 'attribute_name', None))
    print('LINKS', [(l.from_node.name, l.from_socket.name, l.to_node.name, l.to_socket.name) for l in m.node_tree.links])
for o in bpy.data.objects:
    if o.type == 'MESH' and 'chick' in o.name:
        print('VERTEX_COLORS', [(a.name, a.domain, a.data_type) for a in o.data.color_attributes])
