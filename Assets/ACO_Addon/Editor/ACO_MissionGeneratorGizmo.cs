using UnityEditor;
using UnityEngine;
using ACO;

/// 미션 생성기의 작전 공역(노랑), 전투기 집결 범위(주황 점선), 안전지대 + 여유 거리(초록)를 씬 뷰에 그린다(에디터 전용).
/// 플레이 모드에선 이번 미션에 쓰인 유닛(활성 상태)에 생성기에서 점선을 잇는다.
public static class ACO_MissionGeneratorGizmo
{
    private static readonly Color AreaColor = new Color(1f, 0.85f, 0.2f);
    private static readonly Color IngressColor = new Color(1f, 0.55f, 0.1f);
    private static readonly Color SafeColor = new Color(0.3f, 1f, 0.4f);
    private static readonly Color UnitLineColor = new Color(1f, 1f, 1f, 0.35f);
    private static readonly Color CapColor = new Color(0.4f, 0.75f, 1f);

    [DrawGizmo(GizmoType.NonSelected | GizmoType.Selected)]
    private static void Draw(ACO_MissionGenerator gen, GizmoType gizmoType)
    {
        float y = gen.transform.position.y;

        Handles.color = AreaColor;
        DrawRect(gen.AreaMin.x, gen.AreaMin.y, gen.AreaMax.x, gen.AreaMax.y, y, false);

        Handles.color = IngressColor;
        float inset = gen.IngressInset;
        if (gen.AreaMin.x + inset < gen.AreaMax.x - inset && gen.AreaMin.y + inset < gen.AreaMax.y - inset)
        {
            DrawRect(gen.AreaMin.x + inset, gen.AreaMin.y + inset, gen.AreaMax.x - inset, gen.AreaMax.y - inset, y, true);
        }

        if (gen.SafeZones != null)
        {
            Handles.color = SafeColor;
            foreach (Collider zone in gen.SafeZones)
            {
                if (zone == null) { continue; }
                Bounds b = zone.bounds;
                float m = gen.SafeZoneMargin;
                DrawRect(b.min.x - m, b.min.z - m, b.max.x + m, b.max.z + m, b.center.y, false);
                DrawRect(b.min.x, b.min.z, b.max.x, b.max.z, b.center.y, true);
            }
        }

        // CAP 지점: 그 고도에 선회 원(전투기 HoldRadius 기준 대략 600m)과 지면까지 수직선.
        if (gen.CapPoints != null)
        {
            Handles.color = CapColor;
            foreach (Transform cap in gen.CapPoints)
            {
                if (cap == null) { continue; }
                Vector3 p = cap.position;
                Handles.DrawWireDisc(p, Vector3.up, 600f);
                Handles.DrawDottedLine(p, new Vector3(p.x, y, p.z), 6f);
                Handles.Label(p + Vector3.up * 50f, $"CAP  {cap.name}  ({p.y:0}m)");
            }
        }

        if ((gizmoType & GizmoType.Selected) != 0)
        {
            Vector3 labelPos = new Vector3((gen.AreaMin.x + gen.AreaMax.x) * 0.5f, y, gen.AreaMax.y);
            int fighters = gen.Fighters != null ? gen.Fighters.Length : 0;
            int ground = gen.GroundUnits != null ? gen.GroundUnits.Length : 0;
            Handles.Label(labelPos, $"작전 공역  ·  풀: 전투기 {fighters}기 / 지상 {ground}대");
        }

        if (Application.isPlaying)
        {
            Handles.color = UnitLineColor;
            Vector3 origin = gen.transform.position;
            if (gen.Fighters != null)
            {
                foreach (ACO_NPCFighterAI f in gen.Fighters)
                {
                    if (f != null && f.gameObject.activeInHierarchy) { Handles.DrawDottedLine(origin, f.transform.position, 8f); }
                }
            }
            if (gen.GroundUnits != null)
            {
                foreach (UdonSharp.UdonSharpBehaviour u in gen.GroundUnits)
                {
                    if (u != null && u.gameObject.activeInHierarchy) { Handles.DrawDottedLine(origin, u.transform.position, 8f); }
                }
            }
            if (gen.HighValueTargets != null)
            {
                Handles.color = AreaColor;
                foreach (UdonSharp.UdonSharpBehaviour u in gen.HighValueTargets)
                {
                    if (u == null || !u.gameObject.activeInHierarchy) { continue; }
                    Handles.DrawWireDisc(u.transform.position, Vector3.up, gen.DefenseRadius);
                    Handles.Label(u.transform.position + Vector3.up * 60f, "★ " + u.name);
                }
            }
        }
    }

    private static void DrawRect(float minX, float minZ, float maxX, float maxZ, float y, bool dotted)
    {
        Vector3 a = new Vector3(minX, y, minZ);
        Vector3 b = new Vector3(maxX, y, minZ);
        Vector3 c = new Vector3(maxX, y, maxZ);
        Vector3 d = new Vector3(minX, y, maxZ);
        if (dotted)
        {
            Handles.DrawDottedLine(a, b, 6f); Handles.DrawDottedLine(b, c, 6f);
            Handles.DrawDottedLine(c, d, 6f); Handles.DrawDottedLine(d, a, 6f);
        }
        else
        {
            Handles.DrawLine(a, b); Handles.DrawLine(b, c);
            Handles.DrawLine(c, d); Handles.DrawLine(d, a);
        }
    }
}
