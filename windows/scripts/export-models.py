"""Export the existing authored Blender geometry for the Windows software 3D renderer.
Run only in the tool container: blender -b --factory-startup -P windows/scripts/export-models.py
"""
import bpy
import gzip
import json
import math
import re
import runpy
from pathlib import Path

root = Path(__file__).resolve().parents[2]
authoring = root / "artwork/generate_printers.py"
source = {"__file__": str(authoring), "__name__": "bambu_geometry"}
code = authoring.read_text()
# Blender 4.0 lacks this 4.1+ shading helper. The renderer computes its own face normals.
if not hasattr(bpy.types.Mesh, "set_sharp_from_angle"):
    code = re.sub(r"[\w.]+\.set_sharp_from_angle\([^\n]+\)", "pass", code)
exec(compile(code, str(authoring), 'exec'), source)
out = root / "windows/BambuBar.Desktop/Assets/Models"
out.mkdir(parents=True, exist_ok=True)

def srgb(x):
    return round(max(0, min(1, 12.92*x if x <= .0031308 else 1.055*x**(1/2.4)-.055)), 5)

for shape in source["SHAPES"]:
    kit, materials, info = source["build"](shape)
    bpy.context.view_layer.update()
    result = []
    deps = bpy.context.evaluated_depsgraph_get()
    for obj in sorted(kit.model.objects, key=lambda o: o.name):
        if obj.type != 'MESH':
            continue
        path = []
        parent = obj
        while parent:
            path.append(parent.name)
            parent = parent.parent
        evaluated = obj.evaluated_get(deps)
        mesh = bpy.data.meshes.new_from_object(evaluated, depsgraph=deps)
        # Keep the original shape but reduce bevel/cylinder tessellation for efficient 20 fps drawing.
        if len(mesh.polygons) > 100:
            temp = bpy.data.objects.new("WindowsExport", mesh)
            kit.scene.collection.objects.link(temp)
            bpy.context.view_layer.objects.active = temp
            temp.select_set(True)
            mod = temp.modifiers.new("Windows detail", 'DECIMATE')
            budget = 400 if any(key in obj.name for key in ('Shell', 'Lining', 'DoorFrame', 'AMSBase', 'Lid')) else 120
            mod.ratio = min(1, max(.025, budget / len(mesh.polygons)))
            mod.use_collapse_triangulate = True
            temp_eval = temp.evaluated_get(bpy.context.evaluated_depsgraph_get())
            simplified = bpy.data.meshes.new_from_object(temp_eval)
            bpy.data.objects.remove(temp, do_unlink=True)
            bpy.data.meshes.remove(mesh)
            mesh = simplified
        mesh.calc_loop_triangles()
        vertices = [[round(x, 6) for x in (obj.matrix_world @ v.co)] for v in mesh.vertices]
        faces = []
        for tri in mesh.loop_triangles:
            mat = obj.data.materials[tri.material_index] if obj.data.materials and tri.material_index < len(obj.data.materials) else None
            color = [.3, .35, .32, 1]
            if mat:
                shader = mat.node_tree.nodes.get('Principled BSDF') if mat.use_nodes else None
                rgba = shader.inputs['Base Color'].default_value if shader else mat.diffuse_color
                color = [srgb(c) for c in rgba[:3]] + ([.16] if 'Glass' in mat.name else [.5] if 'Smoke' in mat.name else [1])
            faces.append([*tri.vertices, *color])
        name = "/".join(path)
        part = re.search(r"Part_(\d+)", name)
        spool = re.search(r"Filament_(\d+)", obj.name)
        result.append(dict(name=name, vertices=vertices, faces=faces, part=int(part[1]) if part else -1,
                           spool=int(spool[1]) if spool else -1))
        bpy.data.meshes.remove(mesh)
    payload = dict(shape=shape, partHeight=info['part_h'], meshes=result)
    with open(out / f"{shape}.json.gz", 'wb') as file:
        with gzip.GzipFile(filename='', fileobj=file, mode='wb', mtime=0) as zipped:
            zipped.write(json.dumps(payload, separators=(',', ':')).encode())
    print(shape, len(result), 'meshes', sum(len(m['faces']) for m in result), 'triangles', flush=True)
