"""함내 격실 연결 검사. 뜬 부품과 끊어진 조각을 거리로 찾는다.
Blender 안에서 돌린다: blender --background --factory-startup --python Tools/aco_interior_check.py
1) 모듈 안: 메시 조각(이어진 면 묶음)끼리 TOL 이내로 닿는지 본다.
2) 구획 안: 배치된 모듈이 바닥, 천장, 또는 거기 이어진 다른 모듈에 닿는지 본다.
면이 서로 파고들거나, 한쪽이 다른 쪽 안에 있거나, 꼭짓점과 면 거리가 TOL 이내면 닿은 것으로 본다."""
import importlib.util
import os
import sys
import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree
import math

TOOL = os.path.join(os.path.dirname(os.path.abspath(__file__)), "aco_interior_models.py")
spec = importlib.util.spec_from_file_location("aco_interior_models", TOOL)
T = importlib.util.module_from_spec(spec)
spec.loader.exec_module(T)
TOL = 0.004
# 책상 위에 따로 놓이는 물건 묶음: z = 0에 닿는 조각은 책상에 놓인 것으로 본다
ON_SURFACE = ("Int_PCSet", "Int_Station", "Int_ClutterA", "Int_ClutterB")
ON_WALL = ("Int_Notices",)


class Piece:
    def __init__(self, verts, polys):
        self.verts = [Vector(v) for v in verts]
        self.bvh = BVHTree.FromPolygons(self.verts, polys, all_triangles=False)
        a = np.array(verts)
        self.lo, self.hi = a.min(0), a.max(0)


def pieces_of(part):
    out = []
    for bm in part.bm.values():
        bm.verts.ensure_lookup_table()
        bm.verts.index_update()
        seen = set()
        for v in bm.verts:
            if v.index in seen:
                continue
            stack, comp = [v], []
            seen.add(v.index)
            while stack:
                c = stack.pop()
                comp.append(c)
                for e in c.link_edges:
                    o = e.other_vert(c)
                    if o.index not in seen:
                        seen.add(o.index)
                        stack.append(o)
            index = {q.index: i for i, q in enumerate(comp)}
            faces = {f for q in comp for f in q.link_faces}
            out.append(Piece([tuple(q.co) for q in comp], [[index[l.index] for l in f.verts] for f in faces]))
    return out


def gap(a, b):
    """a, b의 최소 거리(꼭짓점-면, 양방향)."""
    best = 1e9
    for p, q in ((a, b), (b, a)):
        for v in p.verts:
            hit = q.bvh.find_nearest(v, best + TOL)
            if hit[0] is not None and hit[3] < best:
                best = hit[3]
                if best <= 1e-5:
                    return best
    return best


def inside(p, q):
    """p의 꼭짓점 하나가 닫힌 메시 q 안에 있는지 광선 교차 수로 본다."""
    o = p.verts[0]
    d = Vector((0.31, 0.53, 0.79)).normalized()
    count = 0
    start = o.copy()
    for _ in range(64):
        hit = q.bvh.ray_cast(start, d)
        if hit[0] is None:
            break
        count += 1
        start = hit[0] + d * 1e-5
    return count % 2 == 1


def touching(a, b):
    if box_gap(a, b) > TOL:
        return False
    if a.bvh.overlap(b.bvh):
        return True
    if gap(a, b) <= TOL:
        return True
    return inside(a, b) or inside(b, a)


def box_gap(a, b):
    return float(np.max(np.maximum(a.lo - b.hi, b.lo - a.hi)))


class Union:
    def __init__(self, n):
        self.p = list(range(n))

    def find(self, x):
        while self.p[x] != x:
            self.p[x] = self.p[self.p[x]]
            x = self.p[x]
        return x

    def join(self, a, b):
        self.p[self.find(a)] = self.find(b)


def check_part(part):
    ps = pieces_of(part)
    if len(ps) < 2:
        return []
    u = Union(len(ps))
    if part.name in ON_WALL:
        for i in range(1, len(ps)):
            u.join(i, 0)
    if part.name in ON_SURFACE:
        grounded = [i for i, q in enumerate(ps) if q.lo[2] <= TOL]
        for i in grounded[1:]:
            u.join(i, grounded[0])
    for i in range(len(ps)):
        for j in range(i + 1, len(ps)):
            if u.find(i) != u.find(j) and touching(ps[i], ps[j]):
                u.join(i, j)
    groups = {}
    for i in range(len(ps)):
        groups.setdefault(u.find(i), []).append(i)
    if len(groups) < 2:
        return []
    main = max(groups.values(), key=len)
    out = []
    for g in groups.values():
        if g is main:
            continue
        lo = np.min([ps[i].lo for i in g], 0)
        hi = np.max([ps[i].hi for i in g], 0)
        d = min(gap(ps[i], ps[j]) for i in g for j in main if box_gap(ps[i], ps[j]) < 0.3) if any(box_gap(ps[i], ps[j]) < 0.3 for i in g for j in main) else 9.9
        out.append((len(g), lo, hi, d))
    return out


def whole(part):
    verts, polys = [], []
    for bm in part.bm.values():
        base = len(verts)
        bm.verts.ensure_lookup_table()
        bm.verts.index_update()
        verts += [tuple(v.co) for v in bm.verts]
        polys += [[base + v.index for v in f.verts] for f in bm.faces]
    return verts, polys


def check_room(name, shells, place, parts, H):
    nodes = []      # (이름, Piece)
    for sh in shells:
        v, f = whole(sh)
        nodes.append((sh.name, Piece(v, f)))
    n_shell = len(nodes)
    for mod, x, y, z, rz in place:
        v, f = whole(parts[mod])
        m = Matrix.Translation((x, y, z)) @ Matrix.Rotation(math.radians(rz), 4, 'Z')
        nodes.append((mod, Piece([tuple(m @ Vector(p)) for p in v], f)))
    n = len(nodes)
    u = Union(n + 1)
    root = n
    for i in range(n_shell):
        u.join(i, root)
    for i, (nm, p) in enumerate(nodes):
        if p.lo[2] <= TOL or p.hi[2] >= H - TOL:
            u.join(i, root)
    for i in range(n):
        for j in range(i + 1, n):
            if u.find(i) != u.find(j) and touching(nodes[i][1], nodes[j][1]):
                u.join(i, j)
    out = []
    for i in range(n):
        if u.find(i) != u.find(root):
            nm, p = nodes[i]
            best = 9.9
            who = ""
            for j in range(n):
                if j != i and u.find(j) == u.find(root) and box_gap(p, nodes[j][1]) < 0.5:
                    d = gap(p, nodes[j][1])
                    if d < best:
                        best, who = d, nodes[j][0]
            out.append((nm, p.lo, p.hi, best, who))
    return n, out


# 벽에 붙는 물건: 늑골, 벽 띠, 문틀과 겹치면 안 된다
WALL_ITEMS = ("Int_Plate_", "Int_Socket", "Int_Junction", "Int_Breaker", "Int_Phone", "Int_Shelf", "Int_Clipboards", "Int_Picture", "Int_Clock", "Int_Speaker",
              "Int_CableCoil", "Int_Frame_Notice", "Int_Extinguisher", "Int_ValveBank", "Int_VestRack", "Int_Fan")
WALL_STRUCTURE = ("Int_Rib_", "Int_RibFlat_", "Int_Trim_140", "Int_HatchFrame", "Int_HatchTrim")
RUN_ITEMS = ("Int_Seal",)            # 관과 트레이가 벽을 지나는 틀은 늑골에 닿으면 안 된다
PANEL_FRAMES = ("Int_Frame_",)      # 깊은 패널 틀은 늑골을 덮어도 되지만 벽 소품과 겹치면 안 된다
CEILING_ITEMS = ("Int_Fixture", "Int_Detector", "Int_ExitSign")
CEILING_STRUCTURE = ("Int_Beam_",)


def world_box(part, m):
    pts = [m @ v.co for bm in part.bm.values() for v in bm.verts]
    a = np.array([tuple(q) for q in pts])
    return a.min(axis=0), a.max(axis=0)


def clash(rooms, parts):
    """벽 소품과 골조, 천장 소품과 보가 겹치는지 범위 상자로 본다."""
    count = 0
    for room, (shells, place) in rooms.items():
        boxes = []
        for name, x, y, z, rz in place:
            if name.startswith(WALL_ITEMS + WALL_STRUCTURE + CEILING_ITEMS + CEILING_STRUCTURE + PANEL_FRAMES + RUN_ITEMS):
                m = Matrix.Translation((x, y, z)) @ Matrix.Rotation(math.radians(rz), 4, 'Z')
                boxes.append((name, world_box(parts[name], m)))
        for pairs in ((WALL_ITEMS, WALL_STRUCTURE), (WALL_ITEMS, PANEL_FRAMES), (CEILING_ITEMS, CEILING_STRUCTURE), (RUN_ITEMS, WALL_STRUCTURE[:2])):
            for name, (lo, hi) in boxes:
                if not name.startswith(pairs[0]):
                    continue
                for other, (lo2, hi2) in boxes:
                    if other != name and other.startswith(pairs[1]) and np.all(lo + 0.002 < hi2) and np.all(lo2 + 0.002 < hi):
                        count += 1
                        print("  %-9s %-18s 가 %-16s 와 겹침 %s" % (room, name, other, np.round((lo + hi) / 2, 2)))
        # 벽 띠는 격실 껍데기에 들어 있어 높이로 본다
        for name, (lo, hi) in boxes:
            if name.startswith(WALL_ITEMS) and lo[2] < T.TRIM_Z + 0.033 and hi[2] > T.TRIM_Z - 0.033:
                count += 1
                print("  %-9s %-18s 가 벽 띠와 겹침 %s" % (room, name, np.round((lo + hi) / 2, 2)))
    print("겹침", count)


def zfight(parts, rooms):
    """같은 방향을 보는 두 면이 1.2mm 안에서 겹치면 Z파이팅이다. 안 보이는 면을 지운 뒤에 본다."""
    place = {}
    for shells, items in rooms.values():
        for sh in shells:
            place[sh.name] = [Matrix.Identity(4)]
        for name, x, y, z, rz in items:
            place.setdefault(name, []).append(Matrix.Translation((x, y, z)) @ Matrix.Rotation(math.radians(rz), 4, 'Z'))
    groups = {}
    for name, mats in place.items():
        for mat, bm in parts[name].bm.items():
            bm.normal_update()
            for f in bm.faces:
                if f.calc_area() < 3e-5:
                    continue
                for pi, m in enumerate(mats):
                    n = (m.to_3x3() @ f.normal).normalized()
                    q = [m @ v.co for v in f.verts]
                    d = n.dot(q[0])
                    a = np.array([tuple(c) for c in q])
                    key = (round(n.x * 50), round(n.y * 50), round(n.z * 50))
                    groups.setdefault(key, []).append((d, name, mat, pi, a.min(axis=0), a.max(axis=0), tuple(n)))
    found = {}
    for key, faces in groups.items():
        faces.sort(key=lambda e: e[0])
        axis = int(np.argmax(np.abs(faces[0][6])))
        keep = [i for i in range(3) if i != axis]
        for i, a in enumerate(faces):
            for b in faces[i + 1:]:
                if b[0] - a[0] > 0.0012:
                    break
                if a[1] == b[1] and a[3] == b[3] and a[2] == b[2]:
                    continue
                w = min(a[5][keep[0]], b[5][keep[0]]) - max(a[4][keep[0]], b[4][keep[0]])
                h = min(a[5][keep[1]], b[5][keep[1]]) - max(a[4][keep[1]], b[4][keep[1]])
                if w > 0.003 and h > 0.003:
                    k = tuple(sorted(((a[1], a[2]), (b[1], b[2]))))
                    e = found.setdefault(k, [0, 0.0, None])
                    e[0] += 1
                    if w * h > e[1]:
                        e[1] = w * h
                        e[2] = (a[4] + a[5]) / 2
    for k, (n, area, where) in sorted(found.items(), key=lambda e: -e[1][1])[:40]:
        print("  %-30s %-30s %4d곳 최대 %.4f㎡ %s" % (k[0][0] + "/" + k[0][1], k[1][0] + "/" + k[1][1], n, area, np.round(where, 2)))
    print("Z파이팅 묶음", len(found))


def leaks(parts, rooms):
    """방 안 여러 지점에서 사방으로 광선을 쏜다. Unity는 뒷면을 그리지 않으므로, 아무것도 안 맞거나 뒷면을 맞으면 그 자리에 구멍이 보인다."""
    place = {}
    for shells, items in rooms.values():
        for sh in shells:
            place[sh.name] = [Matrix.Identity(4)]
        for name, x, y, z, rz in items:
            place.setdefault(name, []).append(Matrix.Translation((x, y, z)) @ Matrix.Rotation(math.radians(rz), 4, 'Z'))
    verts, polys, owner = [], [], []
    for name, mats in place.items():
        for mat, bm in parts[name].bm.items():
            bm.verts.index_update()
            base = [v.co.copy() for v in bm.verts]
            faces = [[v.index for v in f.verts] for f in bm.faces]
            for m in mats:
                off = len(verts)
                verts.extend(m @ c for c in base)
                for f in faces:
                    polys.append([off + i for i in f])
                    owner.append(name + "/" + mat)
    tree = BVHTree.FromPolygons(verts, polys)
    golden = math.pi * (3 - math.sqrt(5))
    dirs = []
    count = 1500
    for i in range(count):
        z = 1 - 2 * (i + 0.5) / count
        r = math.sqrt(1 - z * z)
        dirs.append(Vector((math.cos(i * golden) * r, math.sin(i * golden) * r, z)))
    found = {}
    eyes = rejected = 0
    for lo, hi in T.VOLUMES[:5]:
        x = lo[0] + 0.3
        while x < hi[0] - 0.2:
            y = lo[1] + 0.3
            while y < hi[1] - 0.2:
                for z in (0.5, 1.2, 1.7):
                    o = Vector((x, y, z))
                    res = [tree.ray_cast(o, d) for d in dirs]
                    bad = [(d, r) for d, r in zip(dirs, res) if r[0] is None or r[1].dot(d) > 0]
                    # 가구 속에 들어간 지점은 뺀다
                    if len(bad) > count * 0.15:
                        rejected += 1
                        continue
                    eyes += 1
                    for d, r in bad:
                        key = "(바깥으로 샘)" if r[0] is None else owner[r[2]]
                        e = found.setdefault(key, [0, None, None])
                        e[0] += 1
                        e[1] = tuple(round(v, 2) for v in o)
                        e[2] = tuple(round(v, 2) for v in (r[0] if r[0] is not None else o + d * 3))
                y += 0.8
            x += 0.8
    for key, (n, eye, hit) in sorted(found.items(), key=lambda e: -e[1][0])[:30]:
        print("  %-34s %5d번 눈 %s 맞은 곳 %s" % (key, n, eye, hit))
    print("지점", eyes, "가구 속이라 뺀 지점", rejected, "구멍 묶음", len(found))


def main():
    parts = {p.name: p for p in T.all_parts()}
    print("== 모듈 안 끊어진 조각 ==")
    bad = 0
    seen_chair = False
    for name, p in parts.items():
        if name.startswith("Int_Chair_"):
            if seen_chair:
                continue
            seen_chair = True
        res = check_part(p)
        for count, lo, hi, d in res:
            bad += 1
            print("%-22s 조각 %3d개 %s ~ %s 틈 %.3f" % (name, count, np.round(lo, 3), np.round(hi, 3), d))
    print("모듈 문제", bad)
    print("== 구획 안 뜬 모듈 ==")
    total = 0
    heights = {"Briefing": 3.2, "Cic": 2.8, "Corridor": 2.6, "Berth": 2.6}
    for name, room in T.all_rooms().items():
        H = heights[name]
        n, res = check_room(name, room[0], room[1], parts, H)
        print(name, "모듈", n, "뜸", len(res))
        total += len(res)
        for nm, lo, hi, d, who in res:
            print("  %-20s %s ~ %s 틈 %.3f 가장 가까운 %s" % (nm, np.round(lo, 2), np.round(hi, 2), d, who))
    print("구획 문제", total)
    print("== 벽 소품과 골조 겹침 ==")
    clash(T.all_rooms(), parts)
    print("== 안 보이는 면 지운 뒤 Z파이팅 ==")
    rooms = T.all_rooms()
    pruned = {p.name: p for p in T.all_parts()}
    T.prune_hidden(pruned, rooms)
    zfight(pruned, rooms)
    print("== 뚫린 틈과 뒤집힌 면 ==")
    leaks(pruned, rooms)


if __name__ == "__main__":
    main()
