"""함내 격실의 바닥과 천장 빛을 Blender Cycles로 굽는다.
Blender 안에서 돌린다: blender --background --factory-startup --python Tools/aco_interior_bake.py
만드는 것: Textures/Interior/ACO_Int_BakedLight.exr. 값은 "표면색에 곱하는 밝기"다(1 = 표면색 그대로).
형광등 위치는 레이아웃에서, 색과 세기는 ROOM_LIGHTS에서 온다. 세기는 ACO_InteriorBuilder.RoomLights와 같게 둔다.
Unity 점광원 세기 I는 Blender 와트로 4 pi^2 I다. 형광등과 가구를 옮기면 다시 돌린다.
"""
import importlib.util
import math
import os
import bpy
import numpy as np
from mathutils import Matrix

HERE = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location("aco_interior_models", os.path.join(HERE, "aco_interior_models.py"))
T = importlib.util.module_from_spec(spec)
spec.loader.exec_module(T)

OUTPUT = os.path.join(T.TEXTURE_DIR, "ACO_Int_BakedLight.exr")
SAMPLES = 512
MARGIN = 8
BLUR_FLOOR = 3          # 잡티를 지우는 흐림. 칸 밖으로 번지지 않게 칸마다 따로 한다
BLUR_CEILING = 7
CEILING_LIMIT = 1.25    # 등기구 바로 위가 하얗게 날아가지 않게 천장 밝기를 부드럽게 누른다
WATTS_PER_UNITY = 4 * math.pi ** 2
LIGHT_DROP = 0.13
LIGHT_RADIUS = 0.25
# 격실: (색, Unity 세기)
ROOM_LIGHTS = {
    "Briefing": ((1.0, 0.95, 0.86), 2.2),
    "Cic": ((0.78, 0.88, 1.0), 1.1),
    "Corridor": ((1.0, 0.96, 0.9), 1.5),
    "Berth": ((1.0, 0.92, 0.8), 1.5),
}
# 금속이 아닌 재질의 거칠기는 빛 번짐에 거의 영향이 없어 재질마다 텍스처 색만 쓴다
NORMAL_MAPS = ()


def use_gpu():
    prefs = bpy.context.preferences.addons["cycles"].preferences
    for kind in ("OPTIX", "CUDA"):
        try:
            prefs.compute_device_type = kind
            prefs.get_devices()
            devices = [d for d in prefs.devices if d.type == kind]
            if devices:
                for d in prefs.devices:
                    d.use = d.type == kind
                bpy.context.scene.cycles.device = 'GPU'
                print("BAKE device", kind, [d.name for d in devices])
                return
        except TypeError:
            continue
    print("BAKE device CPU")


def material(name):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    b = nt.nodes["Principled BSDF"]
    path = os.path.join(T.TEXTURE_DIR, "ACO_Int_%s.png" % ("sign" if name == "exit" else name))
    if name.startswith("photo"):
        path = os.path.join(T.TEXTURE_DIR, "Photos", "ACO_Photo_%s.png" % name[5:])
    if os.path.exists(path):
        t = nt.nodes.new("ShaderNodeTexImage")
        t.image = bpy.data.images.load(path)
        nt.links.new(t.outputs[0], b.inputs["Base Color"])
    else:
        b.inputs["Base Color"].default_value = (0.9, 0.9, 0.85, 1)
    b.inputs["Roughness"].default_value = 0.6
    return m


def build_scene(scene, parts, rooms):
    mats = {}

    def get(name):
        if name not in mats:
            mats[name] = material(name)
        return mats[name]

    used = {mat for p in parts.values() for mat in p.bm}
    for name in used:
        get(name)
    targets = []
    for shells, place in rooms.values():
        for sh in shells:
            for o in sh.objects(scene, mats):
                if T.is_light_target(sh.name, o.name.split("__")[1]):
                    targets.append(o)
        for name, x, y, z, rz in place:
            m = Matrix.Translation((x, y, z)) @ Matrix.Rotation(math.radians(rz), 4, 'Z')
            for o in parts[name].objects(scene, mats, m):
                if name == "Int_Fixture":
                    o.visible_shadow = False     # 조명이 등기구 안쪽에 있다
    for room, (color, power) in ROOM_LIGHTS.items():
        for name, x, y, z, rz in rooms[room][1]:
            if name != "Int_Fixture":
                continue
            data = bpy.data.lights.new("lamp", 'POINT')
            data.energy = WATTS_PER_UNITY * power
            data.color = color
            data.shadow_soft_size = LIGHT_RADIUS
            lamp = bpy.data.objects.new("lamp", data)
            lamp.location = (x, y, z - LIGHT_DROP)
            scene.collection.objects.link(lamp)
    return targets, mats


def blur_rects(pixels):
    """칸마다 따로 흐린다. pixels는 아래에서 위로 쌓인 (h, w, 4)."""
    out = pixels.copy()
    size = T.LIGHT_ATLAS
    for (index, kind), (x0, y0, w, h) in T.light_rects().items():
        cell = pixels[y0:y0 + h, x0:x0 + w, :3]
        if kind == 1:
            cell = CEILING_LIMIT * (1 - np.exp(-cell / CEILING_LIMIT))
        radius = BLUR_CEILING if kind == 1 else BLUR_FLOOR
        pad = np.pad(cell, ((radius, radius), (radius, radius), (0, 0)), mode="edge")
        kernel = radius * 2 + 1
        sat = np.cumsum(np.cumsum(np.pad(pad, ((1, 0), (1, 0), (0, 0))), axis=0), axis=1)
        s = sat[kernel:, kernel:] - sat[:-kernel, kernel:] - sat[kernel:, :-kernel] + sat[:-kernel, :-kernel]
        out[y0:y0 + h, x0:x0 + w, :3] = s / (kernel * kernel)
    assert out.shape[0] == size
    return out


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'
    use_gpu()
    scene.cycles.samples = SAMPLES
    scene.cycles.use_denoising = False
    scene.cycles.max_bounces = 4
    scene.cycles.diffuse_bounces = 3
    world = bpy.data.worlds.new("w")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0, 0, 0, 1)
    scene.world = world

    parts = {p.name: p for p in T.all_parts()}
    rooms = T.all_rooms()
    T.prune_hidden(parts, rooms)
    targets, mats = build_scene(scene, parts, rooms)
    print("BAKE targets", len(targets))

    size = T.LIGHT_ATLAS
    image = bpy.data.images.new("BakedLight", size, size, alpha=False, float_buffer=True)
    for name in ("deck", "paint"):
        nt = mats[name].node_tree
        node = nt.nodes.new("ShaderNodeTexImage")
        node.image = image
        nt.nodes.active = node
    bpy.ops.object.select_all(action='DESELECT')
    for o in targets:
        o.select_set(True)
    bpy.context.view_layer.objects.active = targets[0]
    bpy.ops.object.bake(type='DIFFUSE', pass_filter={'DIRECT', 'INDIRECT'}, margin=MARGIN, use_clear=True, uv_layer="LightUV", target='IMAGE_TEXTURES')

    a = np.array(image.pixels[:], dtype=np.float32).reshape(size, size, 4)
    a = blur_rects(a)
    image.pixels[:] = a.ravel()
    image.filepath_raw = OUTPUT
    image.file_format = 'OPEN_EXR'
    scene.render.image_settings.file_format = 'OPEN_EXR'
    scene.render.image_settings.color_depth = '16'
    image.save_render(OUTPUT, scene=scene)
    lit = a[..., :3].mean(axis=2)
    for key, (x0, y0, w, h) in T.light_rects().items():
        cell = lit[y0:y0 + h, x0:x0 + w]
        print("BAKE", key, "min %.3f mean %.3f max %.3f" % (cell.min(), cell.mean(), cell.max()))
    print("BAKE DONE", OUTPUT)


if __name__ == "__main__":
    main()
