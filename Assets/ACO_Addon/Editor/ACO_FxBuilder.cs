using System.Reflection;
using UdonSharp;
using UnityEditor;
using UnityEngine;
using ACO;
using SaccFlightAndVehicles;

/// 폭발·연기·탄환 공용 재질과 파티클 모양. 텍스처는 Tools/aco_fx.py(섬광·충격파·불티)와 Tools/aco_fx_flipbook.py(불덩이·연기·연기 줄기).
/// 파티클 값만 고치고 속도·수명·연사·탄 퍼짐처럼 판정에 묶인 값은 건드리지 않는다.
/// 프리팹 생성 코드와 Setup Check(ACO_PrefabTuning)가 같은 함수를 쓴다.
public static class ACO_FxBuilder
{
    private const string TextureDir = "Assets/ACO_Addon/Textures/FX";
    private const string MaterialDir = "Assets/ACO_Addon/Generated";
    private const string ShaderName = "ACO/FxParticle";
    private const string DebrisMaterialPath = "Assets/Mecha bullet effects/material/Debris.mat";
    private const string RailgunPath = "Assets/ACO_Addon/Prefabs/NPC_Railgun.prefab";
    private const string PreviewPath = "Assets/ACO_Addon/Prefabs/ACO_FxPreview.prefab";

    private static readonly Color EnemyTracer = new Color(1f, 0.84f, 0.18f, 1f);
    private static readonly Color FriendlyTracer = new Color(1f, 0.14f, 0.09f, 1f);
    private static readonly Color RailgunColor = new Color(0.62f, 0.88f, 1f, 1f);
    private const float RibbonTile = 300f;      // 리본 무늬 한 칸 길이(m)
    private const float RibbonRate = 25f;       // 초당 점 수 — 420m/s면 점 간격 17m
    private const float TrailBirthWidth = 0.03f;    // 막 나온 폭(시작 크기에 대한 비율)
    private const float TrailMidWidth = 0.4f;       // TrailGrowTime초 뒤의 폭
    private const float TrailGrowTime = 4f;
    private const float TrailFadeIn = 1.8f;         // 선명해질 때까지(초)
    // 궤적 값은 ACO_Missile_SRAAM에서 손으로 맞춘 값이다: 시작 속도, 방출 모양, 투명도, 노이즈 주파수, 텍스처 모드.
    private const float TrailStartAlpha = 73f / 255f;
    private const float TrailAlpha = 1f;
    private const float TrailAlphaMid = 229f / 255f;    // 수명 45%
    private const float TrailAlphaLate = 169f / 255f;   // 수명 70%
    private const float TrailAlphaEnd = 149f / 255f;    // 수명 88%
    private const float TrailStartSpeed = 10f;
    private const float TrailConeAngle = 2f;
    private const float TrailConeRadius = 0.0001f;
    private const float TrailMissileSize = 50f;
    private const float TrailLargeSize = 65f;
    private const float TrailHeat = 0.35f;          // 엔진 쪽 붉은 기가 남는 시간(초)
    private const float TrailDrift = 4f;            // 옆으로 흐르는 속도(m/s)
    private const float TrailDriftScale = 0.15f;    // 흐름의 공간 주파수 — 약 7m 크기. 점 간격보다 작아 빠를수록 잘게 흔들린다
    private const float TrailDriftScroll = 0.15f;
    private static readonly Color TrailGray = new Color(0.6f, 0.61f, 0.63f, 1f);
    private const float ImpactDistance = 50000f;
    private const float ImpactHeight = 1000f;
    private const float MuzzleRingSize = 1600f;
    private const float MiniRingSize = 80f;
    private const float MiniRingSpacing = 260f;
    private const float MainRingSize = 560f;
    private const float MainRingSizeFar = 220f;
    private const float MainRingSpacing = 1100f;
    private const float MainRingCount = 12f;

    /// 대폭발 크기 배율. 아래 값은 모두 배율 1 기준이고 다시 적용해도 쌓이지 않는다(절댓값을 넣는다).
    private const float BlastScale = 1.5f;

    // ───────────── 재질 ─────────────

    private static void FixTexture(string file, bool repeat)
    {
        string path = $"{TextureDir}/{file}";
        if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) { return; }
        TextureWrapMode wrap = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
        if (importer.wrapMode == wrap && importer.alphaIsTransparency) { return; }
        importer.wrapMode = wrap;
        importer.alphaIsTransparency = true;
        importer.SaveAndReimport();
    }

    /// 있으면 값만 맞춘다(같은 재질을 파티클끼리 함께 쓴다).
    private static Material EnsureMaterial(string name, string texture, bool additive, float boost)
    {
        Shader shader = Shader.Find(ShaderName);
        if (shader == null) { Debug.LogError($"[ACO] 셰이더를 찾지 못했습니다: {ShaderName}"); return null; }
        string path = $"{MaterialDir}/{name}.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(m, path);
        }
        m.shader = shader;
        m.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>($"{TextureDir}/{texture}"));
        m.SetFloat("_Boost", boost);
        m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)(additive ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
        m.SetFloat("_FogToBlack", additive ? 1f : 0f);
        EditorUtility.SetDirty(m);
        return m;
    }

    private static Material Fire() { return EnsureMaterial("ACO_FxFire", "fx_fireball_8x8.png", false, 1.35f); }
    private static Material Smoke() { return EnsureMaterial("ACO_FxSmoke", "fx_smoke_8x8.png", false, 1f); }
    private static Material Flash() { return EnsureMaterial("ACO_FxFlash", "fx_flash.png", true, 2.5f); }
    private static Material Shock() { return EnsureMaterial("ACO_FxShock", "fx_shockwave.png", true, 1.6f); }
    private static Material Spark() { return EnsureMaterial("ACO_FxSpark", "fx_spark.png", true, 3f); }
    private static Material Tracer() { return EnsureMaterial("ACO_FxTracer", "fx_spark.png", true, 3.5f); }
    private static Material Trail() { return EnsureMaterial("ACO_FxTrail", "fx_trail.png", false, 1f); }

    private static Material Foam() { return EnsureMaterial("ACO_FxFoam", "fx_foam.png", false, 1f); }

    /// 미사일 리본. Static 모드라 텍스처가 세계에 붙어 있고, 무늬 한 칸 길이는 이 재질의 Tiling이 정한다.
    private static Material Ribbon()
    {
        Material m = EnsureMaterial("ACO_FxRibbon", "fx_ribbon.png", false, 1f);
        if (m != null) { m.SetTextureScale("_MainTex", new Vector2(1f / RibbonTile, 1f)); }
        return m;
    }

    private static void FixTextures()
    {
        FixTexture("fx_fireball_8x8.png", false);
        FixTexture("fx_smoke_8x8.png", false);
        FixTexture("fx_flash.png", false);
        FixTexture("fx_shockwave.png", false);
        FixTexture("fx_spark.png", false);
        FixTexture("fx_trail.png", true);
        FixTexture("fx_ribbon.png", true);
        FixTexture("fx_foam.png", false);
    }

    // ───────────── 공용 조각 ─────────────

    private static ParticleSystem.Burst[] Bursts(params float[] timeCount)
    {
        var bursts = new ParticleSystem.Burst[timeCount.Length / 2];
        for (int i = 0; i < bursts.Length; i++) { bursts[i] = new ParticleSystem.Burst(timeCount[i * 2], (short)timeCount[i * 2 + 1]); }
        return bursts;
    }

    private static void SetBursts(ParticleSystem ps, float duration, params float[] timeCount)
    {
        ParticleSystem.MainModule main = ps.main;
        main.loop = false;
        main.playOnAwake = false;
        main.duration = duration;
        ParticleSystem.EmissionModule emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(Bursts(timeCount));
    }

    /// 8×8 플립북을 수명 동안 한 번 돈다.
    private static void Flipbook(ParticleSystem ps)
    {
        ParticleSystem.TextureSheetAnimationModule sheet = ps.textureSheetAnimation;
        sheet.enabled = true;
        sheet.mode = ParticleSystemAnimationMode.Grid;
        sheet.numTilesX = 8;
        sheet.numTilesY = 8;
        sheet.animation = ParticleSystemAnimationType.WholeSheet;
        sheet.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, 1f));
        sheet.cycleCount = 1;
    }

    private static void NoFlipbook(ParticleSystem ps)
    {
        ParticleSystem.TextureSheetAnimationModule sheet = ps.textureSheetAnimation;
        sheet.enabled = false;
    }

    private static void Fade(ParticleSystem ps, Color from, Color to, float alpha, float fadeIn)
    {
        ParticleSystem.ColorOverLifetimeModule color = ps.colorOverLifetime;
        color.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(from, 0f), new GradientColorKey(to, 0.45f), new GradientColorKey(to, 1f) },
            new[] { new GradientAlphaKey(fadeIn > 0f ? 0f : alpha, 0f), new GradientAlphaKey(alpha, Mathf.Max(fadeIn, 0.01f)), new GradientAlphaKey(alpha * 0.65f, 0.6f), new GradientAlphaKey(0f, 1f) });
        color.color = new ParticleSystem.MinMaxGradient(gradient);
    }

    private static void Grow(ParticleSystem ps, float from, float to)
    {
        ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
        size.enabled = true;
        var curve = new AnimationCurve(new Keyframe(0f, from, 0f, (to - from) * 3f), new Keyframe(1f, to, 0f, 0f));
        size.size = new ParticleSystem.MinMaxCurve(1f, curve);
    }

    private static void Spin(ParticleSystem ps, float radians)
    {
        ParticleSystem.RotationOverLifetimeModule rotation = ps.rotationOverLifetime;
        rotation.enabled = true;
        rotation.z = new ParticleSystem.MinMaxCurve(-radians, radians);
    }

    private static ParticleSystemRenderer Draw(ParticleSystem ps, Material material, float sortFudge)
    {
        ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
        if (material) { renderer.sharedMaterial = material; }
        renderer.sortMode = ParticleSystemSortMode.Distance;
        renderer.sortingFudge = sortFudge;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return renderer;
    }

    /// 대폭발에 추가하는 자식. Fire.Play()가 함께 돌린다. 있으면 다시 쓴다.
    private static ParticleSystem Child(Transform parent, string name, Vector2 life, Vector2 speed, Vector2 size, float gravity, int max)
    {
        Transform found = parent.Find(name);
        GameObject go = found != null ? found.gameObject : new GameObject(name);
        go.transform.SetParent(parent, false);
        ParticleSystem ps = go.GetComponent<ParticleSystem>();
        if (ps == null) { ps = go.AddComponent<ParticleSystem>(); }
        ParticleSystem.MainModule main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
        main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = Color.white;
        main.gravityModifier = gravity;
        main.maxParticles = max;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        return ps;
    }

    /// 대폭발 파티클의 크기, 튀어 나가는 속도, 방출 반경을 BlastScale로 맞춘다. 있는 모양과 각도는 그대로 둔다.
    private static void Scale(ParticleSystem ps, Vector2 speed, Vector2 size, float radius)
    {
        ParticleSystem.MainModule main = ps.main;
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x * BlastScale, speed.y * BlastScale);
        main.startSize = new ParticleSystem.MinMaxCurve(size.x * BlastScale, size.y * BlastScale);
        ParticleSystem.ShapeModule shape = ps.shape;
        shape.radius = radius * BlastScale;
    }

    private static void Shape(ParticleSystem ps, ParticleSystemShapeType type, float radius, bool faceUp)
    {
        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = type;
        shape.radius = radius;
        ps.transform.localRotation = faceUp ? Quaternion.Euler(-90f, 0f, 0f) : Quaternion.identity;
    }

    // ───────────── 탄환 ─────────────

    /// 멀리서도 1~2픽셀 굵기로 남고, 쏜 쪽을 색으로 알 수 있는 예광탄. 탄속·수명·연사·탄 퍼짐은 그대로 둔다(명중 계산과 맞다).
    public static void StyleTracer(ParticleSystem ps, bool friendly)
    {
        if (ps == null) { return; }
        bool heavy = ps.emission.rateOverTime.constantMax <= 6f;   // 느리게 쏘는 대구경(주포·Flak)
        ParticleSystem.MainModule main = ps.main;
        main.startSize = heavy ? 1.4f : 0.65f;
        main.startColor = friendly ? FriendlyTracer : EnemyTracer;

        ParticleSystem.ColorOverLifetimeModule color = ps.colorOverLifetime;
        color.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.015f), new GradientAlphaKey(1f, 0.85f), new GradientAlphaKey(0f, 1f) });
        color.color = new ParticleSystem.MinMaxGradient(gradient);

        NoFlipbook(ps);
        ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
        Material material = Tracer();
        if (material) { renderer.sharedMaterial = material; }
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.velocityScale = heavy ? 0.05f : 0.03f;
        renderer.lengthScale = 2f;
        renderer.minParticleSize = 0.0014f;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    public static bool IsFriendly(Component c)
    {
        ACO_EscortUnit escort = c.GetComponentInParent<ACO_EscortUnit>(true);
        return escort != null && !escort.Hostile;
    }

    // ───────────── 대폭발 ─────────────

    /// 요새·랜스 포대가 공유하는 대폭발. 터지는 순서는 각 파티클의 Burst가 정하고 ACO_FortressBlast는 Play만 부른다.
    /// 0초 섬광·충격파 → 불덩이 여러 겹 → 불티·연기 줄기·파편 → 땅을 스치는 먼지 고리 → 오래 남는 연기 기둥.
    public static void StyleBlast(ACO_FortressBlast blast)
    {
        if (blast == null) { return; }
        Material fire = Fire();
        Material smoke = Smoke();
        Material trail = Trail();
        Material spark = Spark();

        if (blast.Flash)
        {
            Scale(blast.Flash, Vector2.zero, new Vector2(1800f, 1800f), 0.1f);
            SetBursts(blast.Flash, 0.5f, 0f, 1f);
            NoFlipbook(blast.Flash);
            Fade(blast.Flash, Color.white, Color.white, 1f, 0f);
            Grow(blast.Flash, 0.6f, 1f);
            Draw(blast.Flash, Flash(), -40f).maxParticleSize = 8f;
        }
        if (blast.Shock)
        {
            Scale(blast.Shock, Vector2.zero, new Vector2(3000f, 3000f), 0.1f);
            SetBursts(blast.Shock, 3f, 0f, 1f);
            NoFlipbook(blast.Shock);
            Fade(blast.Shock, Color.white, new Color(1f, 0.82f, 0.66f), 0.9f, 0f);
            Draw(blast.Shock, Shock(), -30f);
        }
        if (blast.Fire)
        {
            ParticleSystem.MainModule main = blast.Fire.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(3f, 6f);
            main.startColor = Color.white;
            Scale(blast.Fire, new Vector2(30f, 160f), new Vector2(240f, 560f), 120f);
            SetBursts(blast.Fire, 2f, 0f, 16f, 0.18f, 10f, 0.45f, 8f, 0.9f, 6f);
            Flipbook(blast.Fire);
            Fade(blast.Fire, Color.white, Color.white, 1f, 0f);
            Grow(blast.Fire, 0.55f, 1.15f);
            Spin(blast.Fire, 0.35f);
            Draw(blast.Fire, fire, -10f).maxParticleSize = 8f;

            ParticleSystem sparks = Child(blast.Fire.transform, "Sparks", new Vector2(1.5f, 3.5f), new Vector2(180f, 620f) * BlastScale, new Vector2(6f, 16f) * BlastScale, 0.5f, 140);
            Shape(sparks, ParticleSystemShapeType.Sphere, 80f * BlastScale, false);
            SetBursts(sparks, 1f, 0.05f, 90f, 0.4f, 30f);
            NoFlipbook(sparks);
            Fade(sparks, new Color(1f, 0.9f, 0.6f), new Color(1f, 0.42f, 0.1f), 1f, 0f);
            ParticleSystemRenderer sparksRenderer = Draw(sparks, spark, -20f);
            sparksRenderer.renderMode = ParticleSystemRenderMode.Stretch;
            sparksRenderer.velocityScale = 0.06f;
            sparksRenderer.lengthScale = 2f;
            sparksRenderer.minParticleSize = 0.001f;

            ParticleSystem streamers = Child(blast.Fire.transform, "Streamers", new Vector2(3f, 5f), new Vector2(260f, 520f) * BlastScale, new Vector2(18f, 34f) * BlastScale, 0.85f, 24);
            Shape(streamers, ParticleSystemShapeType.Hemisphere, 60f * BlastScale, true);
            SetBursts(streamers, 1f, 0.02f, 18f);
            NoFlipbook(streamers);
            Fade(streamers, new Color(1f, 0.75f, 0.4f), new Color(1f, 0.35f, 0.08f), 1f, 0f);
            ParticleSystem.TrailModule trails = streamers.trails;
            trails.enabled = true;
            trails.mode = ParticleSystemTrailMode.PerParticle;
            trails.ratio = 1f;
            trails.lifetime = 0.6f;
            trails.minVertexDistance = 18f * BlastScale;
            trails.worldSpace = true;
            trails.dieWithParticles = false;
            trails.textureMode = ParticleSystemTrailTextureMode.Stretch;
            trails.sizeAffectsWidth = true;
            trails.inheritParticleColor = false;
            trails.widthOverTrail = new ParticleSystem.MinMaxCurve(1.6f, AnimationCurve.Linear(0f, 0.35f, 1f, 1f));
            var trailGradient = new Gradient();
            trailGradient.SetKeys(
                new[] { new GradientColorKey(new Color(0.34f, 0.22f, 0.15f), 0f), new GradientColorKey(new Color(0.16f, 0.15f, 0.15f), 0.4f), new GradientColorKey(new Color(0.22f, 0.21f, 0.2f), 1f) },
                new[] { new GradientAlphaKey(0.95f, 0f), new GradientAlphaKey(0.7f, 0.5f), new GradientAlphaKey(0f, 1f) });
            trails.colorOverTrail = new ParticleSystem.MinMaxGradient(trailGradient);
            ParticleSystemRenderer streamerRenderer = Draw(streamers, spark, -15f);
            streamerRenderer.trailMaterial = trail;

            ParticleSystem ring = Child(blast.Fire.transform, "GroundRing", new Vector2(8f, 14f), new Vector2(220f, 420f) * BlastScale, new Vector2(220f, 420f) * BlastScale, 0f, 40);
            Shape(ring, ParticleSystemShapeType.Circle, 40f * BlastScale, true);
            ParticleSystem.ShapeModule ringShape = ring.shape;
            ringShape.radiusThickness = 0f;
            SetBursts(ring, 1f, 0.1f, 26f);
            Flipbook(ring);
            ParticleSystem.MainModule ringMain = ring.main;
            ringMain.startColor = new ParticleSystem.MinMaxGradient(new Color(0.46f, 0.42f, 0.36f), new Color(0.62f, 0.57f, 0.5f));
            Fade(ring, Color.white, Color.white, 0.8f, 0.04f);
            Grow(ring, 0.5f, 1.5f);
            Spin(ring, 0.15f);
            ParticleSystem.LimitVelocityOverLifetimeModule ringDrag = ring.limitVelocityOverLifetime;
            ringDrag.enabled = true;
            ringDrag.limit = 24f * BlastScale;
            ringDrag.dampen = 0.09f;
            Draw(ring, smoke, 10f).maxParticleSize = 8f;
        }
        if (blast.Debris)
        {
            Scale(blast.Debris, new Vector2(150f, 420f), new Vector2(6f, 20f), 80f);
            SetBursts(blast.Debris, 1f, 0f, 100f);
            Material chunks = AssetDatabase.LoadAssetAtPath<Material>(DebrisMaterialPath);
            ParticleSystem.TextureSheetAnimationModule sheet = blast.Debris.textureSheetAnimation;
            sheet.enabled = chunks != null;
            if (chunks != null)
            {
                sheet.mode = ParticleSystemAnimationMode.Grid;
                sheet.numTilesX = 4;
                sheet.numTilesY = 4;
                sheet.animation = ParticleSystemAnimationType.WholeSheet;
                sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f, 1f);   // 두 상수: 파편마다 칸 하나를 골라 그대로 둔다
            }
            Spin(blast.Debris, 3f);
            Draw(blast.Debris, chunks, 0f);
        }
        if (blast.Smoke)
        {
            ParticleSystem.MainModule main = blast.Smoke.main;
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.15f, 0.14f, 0.14f), new Color(0.3f, 0.28f, 0.27f));
            main.startDelay = 0.4f;
            Scale(blast.Smoke, new Vector2(30f, 70f), new Vector2(300f, 700f), 200f);
            Flipbook(blast.Smoke);
            Fade(blast.Smoke, new Color(1f, 0.62f, 0.36f), Color.white, 0.9f, 0.08f);
            Grow(blast.Smoke, 0.45f, 1.25f);
            Spin(blast.Smoke, 0.08f);
            Draw(blast.Smoke, smoke, 20f).maxParticleSize = 8f;
        }
        if (blast.WaterColumn)
        {
            Scale(blast.WaterColumn, new Vector2(180f, 420f), new Vector2(60f, 180f), 120f);
            SetBursts(blast.WaterColumn, 1f, 0f, 90f, 0.25f, 30f);
            Flipbook(blast.WaterColumn);
            Fade(blast.WaterColumn, Color.white, Color.white, 0.95f, 0f);
            Grow(blast.WaterColumn, 0.6f, 1.3f);
            Draw(blast.WaterColumn, smoke, 0f).maxParticleSize = 8f;
        }
        if (blast.Mist)
        {
            Scale(blast.Mist, new Vector2(30f, 120f), new Vector2(200f, 450f), 150f);
            SetBursts(blast.Mist, 1f, 0f, 30f, 0.4f, 10f);
            Flipbook(blast.Mist);
            Fade(blast.Mist, Color.white, Color.white, 0.6f, 0.05f);
            Grow(blast.Mist, 0.5f, 1.4f);
            Draw(blast.Mist, smoke, 10f).maxParticleSize = 8f;
        }
        if (blast.Secondary)
        {
            ParticleSystem.MainModule main = blast.Secondary.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.6f);
            main.startColor = Color.white;
            Scale(blast.Secondary, new Vector2(10f, 40f), new Vector2(70f, 170f), 20f);
            Flipbook(blast.Secondary);
            Fade(blast.Secondary, Color.white, Color.white, 1f, 0f);
            Grow(blast.Secondary, 0.55f, 1.1f);
            Draw(blast.Secondary, fire, -5f).maxParticleSize = 8f;
        }
    }

    // ───────────── Setup Check ─────────────

    /// 모든 NPC 프리팹의 탄환과 대폭발을 바꾼다. 안에 든 포탑 프리팹은 그 프리팹에서 고치므로 건너뛴다.
    public static void ApplyToPrefabs()
    {
        FixTextures();
        ACO_PrefabTuning.EditPrefabs(root =>
        {
            bool any = false;
            foreach (ACO_NPCGun gun in root.GetComponentsInChildren<ACO_NPCGun>(true))
            {
                if (gun.VisualParticle == null || PrefabUtility.IsPartOfPrefabInstance(gun.VisualParticle)) { continue; }
                StyleTracer(gun.VisualParticle, IsFriendly(gun));
                EditorUtility.SetDirty(gun.VisualParticle);
                any = true;
            }
            foreach (ACO_FortressBlast blast in root.GetComponentsInChildren<ACO_FortressBlast>(true))
            {
                if (PrefabUtility.IsPartOfPrefabInstance(blast)) { continue; }
                StyleBlast(blast);
                any = true;
            }
            return any;
        });
        CreatePreviewPrefab();
    }

    // ───────────── 미사일·폭탄·로켓 폭발 ─────────────

    /// 무장 프리팹 하나의 폭발. 공통(섬광·불덩이·불티·연기)은 ExplosionObject 안에 두고 어디서 터지든 켠다.
    /// 땅(먼지 고리·파편)과 물(물기둥·물안개)은 Fx_Ground·Fx_Water에 두고 그 면에 맞았을 때 더 켠다. 예전 파티클 fire는 끄기만 한다.
    public static void StyleWeapon(GameObject root)
    {
        if (root == null) { return; }
        ACO_Missile missile = root.GetComponent<ACO_Missile>();
        ACO_Ordnance ordnance = root.GetComponent<ACO_Ordnance>();
        GameObject explosion = missile != null ? missile.ExplosionObject : (ordnance != null ? ordnance.ExplosionObject : null);
        if (explosion == null) { return; }
        float scale = WeaponScale(root.name, ordnance);
        float duration = missile != null ? missile.ExplosionDuration : ordnance.ExplosionDuration;
        float life = Mathf.Min(1f, Mathf.Max(duration - 0.3f, 0.5f) / 5.5f);   // 탄이 풀로 돌아가면 파티클도 꺼진다

        Transform old = explosion.transform.Find("fire");
        if (old != null) { old.gameObject.SetActive(false); }
        BuildCommon(explosion.transform, scale, life);

        GameObject ground = SurfaceRoot(root.transform, "Fx_Ground");
        BuildGround(ground.transform, scale, life);
        GameObject water = SurfaceRoot(root.transform, "Fx_Water");
        BuildWater(water.transform, scale, life);
        if (missile != null)
        {
            missile.GroundExplosionObject = ground;
            missile.WaterExplosionObject = water;
            EditorUtility.SetDirty(missile);
        }
        else
        {
            ordnance.GroundExplosionObject = ground;
            ordnance.WaterExplosionObject = water;
            EditorUtility.SetDirty(ordnance);
        }
        StyleTrails(missile != null ? missile.TrailParticles : ordnance.TrailParticles, duration);
        ACO_Dispenser dispenser = root.GetComponentInChildren<ACO_Dispenser>(true);
        if (dispenser != null && dispenser.Blasts != null)
        {
            ParticleSystem.MainModule main = dispenser.Blasts.main;
            main.startColor = Color.white;
            Flipbook(dispenser.Blasts);
            Fade(dispenser.Blasts, Color.white, Color.white, 1f, 0f);
            Draw(dispenser.Blasts, Fire(), 0f).maxParticleSize = 8f;
        }
    }

    /// 연기 궤적: 리본 구조와 최대 폭, 수명은 그대로 둔다. 막 나온 곳은 좁고 흐리며, 뒤로 갈수록 넓어지고 선명해진다.
    /// 리본은 점을 순서대로 잇는다 — 점이 제각각 움직이면 띠가 접히므로 무작위 방향을 없애고, 이웃한 점이 함께 움직이는 큰 흐름만 둔다.
    /// keepTime = 터진 뒤 풀로 돌아가기까지의 시간. 수명이 그보다 길면 남은 연기가 한순간에 사라지므로 그 안으로 줄인다.
    private static void StyleTrails(ParticleSystem[] trails, float keepTime)
    {
        if (trails == null) { return; }
        foreach (ParticleSystem ps in trails)
        {
            if (ps == null) { continue; }
            ParticleSystem.MainModule main = ps.main;
            main.maxParticles = Mathf.Max(main.maxParticles, Mathf.CeilToInt(RibbonRate * main.startLifetime.constantMax) + 20);
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = RibbonRate;
            float life = Mathf.Max(Mathf.Min(main.startLifetime.constantMax, keepTime - 0.2f), 0.1f);
            main.startLifetime = life;
            float heat = Mathf.Clamp(TrailHeat / life, 0.005f, 0.06f);
            float fadeIn = Mathf.Clamp(TrailFadeIn / life, 0.05f, 0.3f);
            float grow = Mathf.Clamp(TrailGrowTime / life, 0.1f, 0.6f);
            main.startSpeed = TrailStartSpeed;
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            if (ps.GetComponentInParent<ACO_Ordnance>(true) == null) { shape.angle = TrailConeAngle; }
            AimTrail(ps);
            ParticleSystem.NoiseModule noise = ps.noise;
            noise.enabled = true;
            noise.damping = false;
            noise.strength = TrailDrift;
            noise.frequency = TrailDriftScale;
            noise.scrollSpeed = TrailDriftScroll;
            noise.octaveCount = 2;
            noise.octaveMultiplier = 0.5f;
            noise.octaveScale = 2f;
            ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
            size.enabled = true;
            var width = new AnimationCurve(
                new Keyframe(0f, TrailBirthWidth, 0f, (TrailMidWidth - TrailBirthWidth) / grow * 1.4f),
                new Keyframe(grow, TrailMidWidth, 1f - TrailBirthWidth, 1f - TrailBirthWidth),
                new Keyframe(1f, 1f, 0.4f, 0f));
            size.size = new ParticleSystem.MinMaxCurve(1f, width);
            ParticleSystem.ColorOverLifetimeModule color = ps.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(new Color(1f, 0.62f, 0.34f), 0f), new GradientColorKey(TrailGray, heat), new GradientColorKey(TrailGray, 1f) },
                new[] { new GradientAlphaKey(TrailStartAlpha, 0f), new GradientAlphaKey(TrailAlpha, fadeIn), new GradientAlphaKey(TrailAlphaMid, 0.45f), new GradientAlphaKey(TrailAlphaLate, 0.7f), new GradientAlphaKey(TrailAlphaEnd, 0.88f), new GradientAlphaKey(0f, 1f) });
            color.color = new ParticleSystem.MinMaxGradient(gradient);
            ParticleSystem.TrailModule module = ps.trails;
            module.textureMode = ParticleSystemTrailTextureMode.Tile;
            module.inheritParticleColor = true;
            module.colorOverLifetime = Color.white;
            module.colorOverTrail = Color.white;
            ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.trailMaterial = Ribbon();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            EditorUtility.SetDirty(ps);
        }
    }

    /// 궤적은 탄 뒤로 나간다(시작 속도가 있어 방향이 보인다). 이미 뒤를 향하면 손으로 돌린 값을 그대로 둔다. 미사일은 등급별 폭을 넣고, 로켓·포탄의 폭은 프리팹 값을 쓴다.
    private static void AimTrail(ParticleSystem ps)
    {
        Transform root = ps.transform.root;
        if (Vector3.Dot(ps.transform.forward, root.forward) > 0f) { ps.transform.rotation = root.rotation * Quaternion.Euler(180f, 0f, 0f); }
        ParticleSystem.ShapeModule shape = ps.shape;
        shape.radius = TrailConeRadius;
        if (ps.GetComponentInParent<ACO_Ordnance>(true) != null) { return; }
        ParticleSystem.MainModule main = ps.main;
        main.startSize = IsMicroMissile(root.name) ? TrailMissileSize * 0.45f : IsLargeMissile(root.name) ? TrailLargeSize : TrailMissileSize;
    }

    /// 마이크로미사일(MMP): 궤적이 가늘고 폭발이 작다.
    private static bool IsMicroMissile(string name) { return name.Contains("MMP"); }

    /// 레일건 포드 탄(RGP): 연기 대신 빛줄기, 폭발이 작다.
    private static bool IsRailSlug(string name) { return name.Contains("RGP"); }

    // ───────────── 레이저 에이스 ─────────────

    private static readonly Color LaserColor = new Color(1f, 0.32f, 0.22f, 1f);

    /// 레이저 빔 재질(가산 — 빛줄기 텍스처). LineRenderer가 쓴다.
    public static Material LaserBeamMaterial() { return EnsureMaterial("ACO_FxLaserBeam", "fx_trail.png", true, 3.5f); }

    /// 빔이 닿은 곳의 불티(월드 — 스크립트가 Emit). 있으면 값만 다시 맞춘다.
    public static ParticleSystem BuildLaserSparks(Transform parent)
    {
        ParticleSystem ps = Part(parent, "LaserSparks", new Vector2(0.2f, 0.5f), new Vector2(8f, 40f), new Vector2(0.4f, 1.2f), 0.4f, 120);
        Shape(ps, ParticleSystemShapeType.Sphere, 0.6f, false);
        ParticleSystem.EmissionModule em = ps.emission;
        em.rateOverTime = 0f;
        ParticleSystem.MainModule main = ps.main;
        main.playOnAwake = true;
        main.loop = true;
        Fade(ps, Color.white, LaserColor, 1f, 0f);
        ParticleSystemRenderer r = Draw(ps, Spark(), -20f);
        r.renderMode = ParticleSystemRenderMode.Stretch;
        r.velocityScale = 0.05f;
        r.lengthScale = 1f;
        EditorUtility.SetDirty(ps);
        return ps;
    }

    /// 탄도미사일 연출(ACO_IcbmSite): 엔진 불꽃(미사일 꼬리, 계속), 연기 궤적(월드, 날아간 거리마다), 발사 연기(사일로, 한 번), 하늘 자폭(섬광·불덩이).
    /// 있으면 값만 다시 맞춘다.
    public static void BuildIcbmFx(ACO_IcbmSite site)
    {
        if (site == null || site.Missile == null) { return; }
        Transform tail = site.Missile;

        ParticleSystem flame = Part(tail, "Flame", new Vector2(0.25f, 0.45f), new Vector2(30f, 55f), new Vector2(5f, 9f), 0f, 60);
        flame.transform.localPosition = new Vector3(0f, -0.5f, 0f);
        flame.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);   // 원뿔이 아래(-Y)로
        ParticleSystem.ShapeModule fs = flame.shape;
        fs.enabled = true;
        fs.shapeType = ParticleSystemShapeType.Cone;
        fs.angle = 6f;
        fs.radius = 0.7f;
        ParticleSystem.EmissionModule fe = flame.emission;
        fe.rateOverTime = 70f;
        ParticleSystem.MainModule fm = flame.main;
        fm.loop = true;
        fm.playOnAwake = true;
        fm.simulationSpace = ParticleSystemSimulationSpace.Local;
        Fade(flame, Color.white, new Color(1f, 0.55f, 0.2f), 1f, 0f);
        Grow(flame, 0.7f, 1.3f);
        Draw(flame, Flash(), -10f).maxParticleSize = 4f;
        site.Flame = flame.gameObject;

        // 연기 궤적은 미사일 밖(사이트 아래)에 둔다 — 미사일을 숨겨도 남은 연기가 사라지지 않게. 사이트가 매 프레임 미사일 꼬리로 옮긴다
        ParticleSystem trail = Part(site.transform, "Trail", new Vector2(18f, 26f), new Vector2(0f, 2f), new Vector2(14f, 22f), -0.003f, 900);
        ParticleSystem.ShapeModule ts = trail.shape;
        ts.enabled = true;
        ts.shapeType = ParticleSystemShapeType.Sphere;
        ts.radius = 1.5f;
        ParticleSystem.EmissionModule te = trail.emission;
        te.rateOverTime = 0f;
        te.rateOverDistance = 1f / 18f;
        ParticleSystem.MainModule tm = trail.main;
        tm.loop = true;
        tm.playOnAwake = false;
        tm.emitterVelocityMode = ParticleSystemEmitterVelocityMode.Transform;
        tm.startColor = new ParticleSystem.MinMaxGradient(new Color(0.78f, 0.78f, 0.76f), new Color(0.92f, 0.92f, 0.9f));
        Flipbook(trail);
        Fade(trail, Color.white, Color.white, 0.85f, 0.02f);
        Grow(trail, 0.5f, 3.2f);
        Spin(trail, 0.15f);
        Draw(trail, Smoke(), 0f).maxParticleSize = 8f;
        site.Trail = trail;

        Transform root = site.transform;
        ParticleSystem launch = Part(root, "LaunchSmoke", new Vector2(6f, 12f), new Vector2(20f, 60f), new Vector2(25f, 45f), -0.01f, 80);
        Shape(launch, ParticleSystemShapeType.Hemisphere, 4f, true);
        SetBursts(launch, 12f, 0f, 40f, 0.5f, 20f, 1.2f, 15f);
        ParticleSystem.MainModule lm = launch.main;
        lm.playOnAwake = false;
        lm.startColor = new ParticleSystem.MinMaxGradient(new Color(0.75f, 0.74f, 0.72f), new Color(0.9f, 0.9f, 0.88f));
        Flipbook(launch);
        Fade(launch, Color.white, Color.white, 0.9f, 0f);
        Grow(launch, 0.6f, 2.2f);
        Draw(launch, Smoke(), 0f).maxParticleSize = 8f;
        site.LaunchSmoke = launch;

        ParticleSystem burst = Part(root, "SkyBurst", new Vector2(0.6f, 0.6f), Vector2.zero, new Vector2(5000f, 5000f), 0f, 2);
        Shape(burst, ParticleSystemShapeType.Sphere, 1f, false);
        SetBursts(burst, 6f, 0f, 1f);
        ParticleSystem.MainModule bm = burst.main;
        bm.playOnAwake = false;
        Fade(burst, Color.white, new Color(1f, 0.8f, 0.6f), 1f, 0f);
        Grow(burst, 0.4f, 1f);
        Draw(burst, Flash(), -30f).maxParticleSize = 1000f;
        ParticleSystem fire = Part(burst.transform, "Fire", new Vector2(3f, 5f), new Vector2(40f, 220f), new Vector2(900f, 1800f), 0f, 24);
        Shape(fire, ParticleSystemShapeType.Sphere, 150f, false);
        SetBursts(fire, 6f, 0f, 18f);
        ParticleSystem.MainModule firem = fire.main;
        firem.playOnAwake = false;
        Flipbook(fire);
        Fade(fire, Color.white, new Color(0.3f, 0.25f, 0.22f), 1f, 0f);
        Grow(fire, 0.5f, 1.4f);
        Draw(fire, Fire(), -10f).maxParticleSize = 1000f;
        site.SkyBurst = burst;
        EditorUtility.SetDirty(site);
    }

    // ───────────── 레일건 포드(RGP) ─────────────

    /// RGP 탄 연출 — 소형 레일건(랜스 포대·육상전함 주포)과 같게, 공중 폭발만 없이. StyleWeapon 다음에 부른다(StyleWeapon이 연기 궤적으로 맞춘 값을 덮는다).
    ///  - 궤적: 연기 대신 짧게 남는 청백색 빛줄기(가산 혼합, 흔들림 없음). 탄이 4km/s라 0.5초 수명이면 뒤로 2km 남는다.
    ///  - 고리: 날아간 거리 RgpRingSpacing마다 진행 방향을 보는 충격파 고리(소형 레일건 고리와 같은 재질·색, 크기만 작게). 궤적처럼 발사마다 지우고 다시 켠다(TrailParticles에 넣는다).
    ///  - 명중: 섬광·불티만(청백색). 불덩이·연기는 끈다. 땅·물에 맞은 흙먼지·물기둥은 작게 둔다.
    private const float RgpRingSize = 36f;
    private const float RgpRingSpacing = 180f;

    public static void StyleRailSlug(GameObject root)
    {
        ACO_Missile missile = root != null ? root.GetComponent<ACO_Missile>() : null;
        if (missile == null || missile.TrailParticles == null) { return; }
        if (missile.ExplosionObject != null)
        {
            Transform ex = missile.ExplosionObject.transform;
            foreach (string off in new[] { "Fx_Fire", "Fx_Smoke" })
            {
                Transform t = ex.Find(off);
                if (t != null) { t.gameObject.SetActive(false); }
            }
            Transform f = ex.Find("Fx_Flash");
            if (f != null) { Fade(f.GetComponent<ParticleSystem>(), Color.white, RailgunColor, 1f, 0f); }
            Transform s = ex.Find("Fx_Sparks");
            if (s != null) { Fade(s.GetComponent<ParticleSystem>(), Color.white, RailgunColor, 1f, 0f); }
        }
        Material glow = EnsureMaterial("ACO_FxRailTrail", "fx_trail.png", true, 3f);
        foreach (ParticleSystem ps in missile.TrailParticles)
        {
            if (ps == null) { continue; }
            ParticleSystem.MainModule main = ps.main;
            main.startLifetime = 0.5f;
            main.startSize = TrailMissileSize * 0.22f;
            main.startSpeed = 0f;
            ParticleSystem.NoiseModule noise = ps.noise;
            noise.enabled = false;
            ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.15f)));
            ParticleSystem.ColorOverLifetimeModule color = ps.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(RailgunColor, 0.25f), new GradientColorKey(RailgunColor, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.3f), new GradientAlphaKey(0f, 1f) });
            color.color = new ParticleSystem.MinMaxGradient(gradient);
            ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.trailMaterial = glow;
            EditorUtility.SetDirty(ps);
        }

        ParticleSystem rings = Ring(root.transform, "Fx_RailRings", new Vector2(0.9f, 1.4f));
        rings.transform.localPosition = new Vector3(0f, 0f, -0.5f);
        rings.transform.localRotation = Quaternion.identity;
        ParticleSystem.MainModule rm = rings.main;
        rm.startSize = RgpRingSize;
        rm.loop = true;
        rm.playOnAwake = false;
        rm.simulationSpace = ParticleSystemSimulationSpace.World;
        rm.emitterVelocityMode = ParticleSystemEmitterVelocityMode.Transform;
        ParticleSystem.EmissionModule re = rings.emission;
        re.rateOverTime = 0f;
        re.rateOverDistance = 1f / RgpRingSpacing;
        // 고리가 진행 방향을 보게(Velocity 정렬) 탄 속도를 조금 물려받는다 — 0이면 방향이 없다
        ParticleSystem.InheritVelocityModule iv = rings.inheritVelocity;
        iv.enabled = true;
        iv.mode = ParticleSystemInheritVelocityMode.Initial;
        iv.curveMultiplier = 0.01f;
        var list = new System.Collections.Generic.List<ParticleSystem>(missile.TrailParticles);
        if (!list.Contains(rings)) { list.Add(rings); }
        missile.TrailParticles = list.ToArray();
        EditorUtility.SetDirty(rings);
        EditorUtility.SetDirty(missile);
    }

    /// RGP 포구 섬광(muzzle 자식 RGP_Flash): 흰 섬광 하나 + 앞으로 튀는 청백색 불티 + 작은 충격파. 발사마다 모듈(LaunchFlash)이 Play한다. 있으면 값만 다시 맞춘다.
    public static ParticleSystem BuildRailMuzzleFlash(Transform muzzle)
    {
        ParticleSystem flash = Part(muzzle, "RGP_Flash", new Vector2(0.12f, 0.12f), Vector2.zero, new Vector2(7f, 7f), 0f, 2);
        Shape(flash, ParticleSystemShapeType.Sphere, 0.05f, false);
        SetBursts(flash, 0.2f, 0f, 1f);
        Fade(flash, Color.white, RailgunColor, 1f, 0f);
        Grow(flash, 0.5f, 1f);
        Draw(flash, Flash(), -30f).maxParticleSize = 4f;
        ParticleSystem.MainModule fm = flash.main;
        fm.playOnAwake = false;

        ParticleSystem sparks = Part(flash.transform, "Sparks", new Vector2(0.12f, 0.3f), new Vector2(60f, 160f), new Vector2(0.25f, 0.5f), 0f, 16);
        ParticleSystem.ShapeModule shape = sparks.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 9f;
        shape.radius = 0.04f;
        sparks.transform.localRotation = Quaternion.identity;
        SetBursts(sparks, 0.3f, 0f, 10f);
        Fade(sparks, Color.white, RailgunColor, 1f, 0f);
        ParticleSystemRenderer sr = Draw(sparks, Spark(), -20f);
        sr.renderMode = ParticleSystemRenderMode.Stretch;
        sr.velocityScale = 0.04f;
        sr.lengthScale = 1f;
        ParticleSystem.MainModule sm = sparks.main;
        sm.playOnAwake = false;

        ParticleSystem ring = Part(flash.transform, "Shock", new Vector2(0.22f, 0.22f), Vector2.zero, new Vector2(3f, 3f), 0f, 2);
        Shape(ring, ParticleSystemShapeType.Sphere, 0.02f, false);
        SetBursts(ring, 0.3f, 0f, 1f);
        Fade(ring, Color.white, RailgunColor, 0.8f, 0f);
        Grow(ring, 0.3f, 2.2f);
        Draw(ring, Shock(), -25f).maxParticleSize = 4f;
        ParticleSystem.MainModule rm = ring.main;
        rm.playOnAwake = false;
        EditorUtility.SetDirty(flash);
        return flash;
    }

    private static bool IsLargeMissile(string name)
    {
        return name.Contains("LAGM") || name.Contains("LASM") || name.Contains("VLS") || name.Contains("LAAM") || name.Contains("ADAM");
    }

    /// 크기 등급: 폭탄 > 대함·대지 미사일·산탄 > 공대공 미사일 > 로켓·포탄.
    private static float WeaponScale(string name, ACO_Ordnance ordnance)
    {
        if (ordnance != null) { return ordnance.Acceleration <= 0f && ordnance.SplashRadius >= 150f ? 3.2f : 0.6f; }
        if (IsMicroMissile(name)) { return 0.6f; }
        if (IsRailSlug(name)) { return 0.45f; }
        return name.Contains("AGM") || name.Contains("ASM") || name.Contains("SCM") ? 1.5f : 1f;
    }

    private static GameObject SurfaceRoot(Transform parent, string name)
    {
        Transform found = parent.Find(name);
        GameObject go = found != null ? found.gameObject : new GameObject(name);
        go.transform.SetParent(parent, false);
        go.layer = parent.gameObject.layer;
        go.SetActive(false);
        return go;
    }

    /// 켜지는 순간 재생하고, 탄 크기와 상관없이 값 그대로의 크기로 그린다.
    private static ParticleSystem Part(Transform parent, string name, Vector2 life, Vector2 speed, Vector2 size, float gravity, int max)
    {
        ParticleSystem ps = Child(parent, name, life, speed, size, gravity, max);
        ps.gameObject.layer = parent.gameObject.layer;
        ParticleSystem.MainModule main = ps.main;
        main.scalingMode = ParticleSystemScalingMode.Local;
        return ps;
    }

    private static void AutoPlay(ParticleSystem ps)
    {
        ParticleSystem.MainModule main = ps.main;
        main.playOnAwake = true;
    }

    private static void BuildCommon(Transform parent, float k, float life)
    {
        ParticleSystem flash = Part(parent, "Fx_Flash", new Vector2(0.18f, 0.18f), Vector2.zero, new Vector2(95f, 95f) * k, 0f, 1);
        Shape(flash, ParticleSystemShapeType.Sphere, 0.1f, false);
        SetBursts(flash, 0.2f, 0f, 1f);
        Fade(flash, Color.white, Color.white, 1f, 0f);
        Grow(flash, 0.6f, 1f);
        Draw(flash, Flash(), -30f).maxParticleSize = 8f;
        AutoPlay(flash);

        ParticleSystem fire = Part(parent, "Fx_Fire", new Vector2(1.1f, 2.2f) * Mathf.Max(life, 0.6f), new Vector2(3f, 14f) * k, new Vector2(34f, 62f) * k, -0.02f, 8);
        Shape(fire, ParticleSystemShapeType.Sphere, 5f * k, false);
        SetBursts(fire, 0.5f, 0f, 4f, 0.06f, 2f, 0.14f, 1f);
        Flipbook(fire);
        Fade(fire, Color.white, Color.white, 1f, 0f);
        Grow(fire, 0.6f, 1.15f);
        Spin(fire, 0.35f);
        Draw(fire, Fire(), -10f).maxParticleSize = 8f;
        AutoPlay(fire);

        ParticleSystem sparks = Part(parent, "Fx_Sparks", new Vector2(0.5f, 1.4f), new Vector2(40f, 150f) * k, new Vector2(1.2f, 2.6f) * k, 0.4f, 24);
        Shape(sparks, ParticleSystemShapeType.Sphere, 3f * k, false);
        SetBursts(sparks, 0.5f, 0.02f, 18f);
        Fade(sparks, new Color(1f, 0.9f, 0.6f), new Color(1f, 0.42f, 0.1f), 1f, 0f);
        ParticleSystemRenderer sparksRenderer = Draw(sparks, Spark(), -20f);
        sparksRenderer.renderMode = ParticleSystemRenderMode.Stretch;
        sparksRenderer.velocityScale = 0.06f;
        sparksRenderer.lengthScale = 2f;
        sparksRenderer.minParticleSize = 0.001f;
        AutoPlay(sparks);

        ParticleSystem smoke = Part(parent, "Fx_Smoke", new Vector2(3f, 5.5f) * life, new Vector2(2f, 9f) * k, new Vector2(40f, 80f) * k, -0.02f, 8);
        Shape(smoke, ParticleSystemShapeType.Sphere, 8f * k, false);
        SetBursts(smoke, 0.6f, 0.2f, 3f, 0.45f, 2f);
        ParticleSystem.MainModule smokeMain = smoke.main;
        smokeMain.startColor = new ParticleSystem.MinMaxGradient(new Color(0.2f, 0.19f, 0.18f), new Color(0.36f, 0.34f, 0.33f));
        Flipbook(smoke);
        Fade(smoke, new Color(1f, 0.66f, 0.42f), Color.white, 0.85f, 0.08f);
        Grow(smoke, 0.5f, 1.3f);
        Spin(smoke, 0.12f);
        Draw(smoke, Smoke(), 10f).maxParticleSize = 8f;
        AutoPlay(smoke);
    }

    private static void BuildGround(Transform parent, float k, float life)
    {
        ParticleSystem ring = Part(parent, "Fx_Dust", new Vector2(2f, 3.6f) * Mathf.Max(life, 0.7f), new Vector2(30f, 70f) * k, new Vector2(28f, 55f) * k, 0f, 10);
        Shape(ring, ParticleSystemShapeType.Circle, 3f * k, true);
        ParticleSystem.ShapeModule ringShape = ring.shape;
        ringShape.radiusThickness = 0f;
        SetBursts(ring, 0.5f, 0.02f, 8f);
        ParticleSystem.MainModule ringMain = ring.main;
        ringMain.startColor = new ParticleSystem.MinMaxGradient(new Color(0.46f, 0.42f, 0.36f), new Color(0.62f, 0.57f, 0.5f));
        Flipbook(ring);
        Fade(ring, Color.white, Color.white, 0.8f, 0.05f);
        Grow(ring, 0.5f, 1.4f);
        Spin(ring, 0.15f);
        ParticleSystem.LimitVelocityOverLifetimeModule drag = ring.limitVelocityOverLifetime;
        drag.enabled = true;
        drag.limit = 4f * k;
        drag.dampen = 0.09f;
        Draw(ring, Smoke(), 5f).maxParticleSize = 8f;
        AutoPlay(ring);

        ParticleSystem debris = Part(parent, "Fx_Debris", new Vector2(1.5f, 2.8f) * Mathf.Max(life, 0.7f), new Vector2(30f, 90f) * k, new Vector2(0.8f, 2.4f) * k, 1f, 16);
        Shape(debris, ParticleSystemShapeType.Cone, 2f * k, true);
        ParticleSystem.ShapeModule debrisShape = debris.shape;
        debrisShape.angle = 42f;
        SetBursts(debris, 0.5f, 0f, 14f);
        Material chunks = AssetDatabase.LoadAssetAtPath<Material>(DebrisMaterialPath);
        ParticleSystem.TextureSheetAnimationModule sheet = debris.textureSheetAnimation;
        sheet.enabled = chunks != null;
        if (chunks != null)
        {
            sheet.mode = ParticleSystemAnimationMode.Grid;
            sheet.numTilesX = 4;
            sheet.numTilesY = 4;
            sheet.animation = ParticleSystemAnimationType.WholeSheet;
            sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f, 1f);   // 두 상수: 파편마다 칸 하나를 골라 그대로 둔다
        }
        Spin(debris, 3f);
        Draw(debris, chunks, 0f);
        AutoPlay(debris);
    }

    private static void BuildWater(Transform parent, float k, float life)
    {
        ParticleSystem column = Part(parent, "Fx_Column", new Vector2(1.2f, 2.4f) * Mathf.Max(life, 0.7f), new Vector2(50f, 110f) * k, new Vector2(14f, 34f) * k, 0.8f, 16);
        Shape(column, ParticleSystemShapeType.Cone, 3f * k, true);
        ParticleSystem.ShapeModule columnShape = column.shape;
        columnShape.angle = 10f;
        SetBursts(column, 0.5f, 0f, 14f);
        ParticleSystem.MainModule columnMain = column.main;
        columnMain.startColor = new Color(0.86f, 0.92f, 0.96f);
        Flipbook(column);
        Fade(column, Color.white, Color.white, 0.95f, 0f);
        Grow(column, 0.6f, 1.3f);
        Draw(column, Smoke(), 0f).maxParticleSize = 8f;
        AutoPlay(column);

        ParticleSystem mist = Part(parent, "Fx_Mist", new Vector2(2.5f, 4.2f) * Mathf.Max(life, 0.6f), new Vector2(8f, 26f) * k, new Vector2(40f, 70f) * k, 0f, 8);
        Shape(mist, ParticleSystemShapeType.Circle, 4f * k, true);
        SetBursts(mist, 0.5f, 0.05f, 6f);
        ParticleSystem.MainModule mistMain = mist.main;
        mistMain.startColor = new Color(0.86f, 0.92f, 0.96f);
        Flipbook(mist);
        Fade(mist, Color.white, Color.white, 0.6f, 0.05f);
        Grow(mist, 0.5f, 1.4f);
        Draw(mist, Smoke(), 5f).maxParticleSize = 8f;
        AutoPlay(mist);
    }

    /// 연기 궤적만 다시 적용한다.
    /// 방향, 방출 반경, 시작 크기만 고친다. 프리팹을 손으로 맞춘 나머지 값은 건드리지 않는다.
    public static void ApplyTrailAim()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null || (asset.GetComponent<ACO_Missile>() == null && asset.GetComponent<ACO_Ordnance>() == null)) { continue; }
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                ACO_Missile m = root.GetComponent<ACO_Missile>();
                ParticleSystem[] trails = m != null ? m.TrailParticles : root.GetComponent<ACO_Ordnance>().TrailParticles;
                if (trails == null) { continue; }
                foreach (ParticleSystem ps in trails)
                {
                    if (ps == null) { continue; }
                    AimTrail(ps);
                    EditorUtility.SetDirty(ps);
                }
                PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log($"[ACO] 연기 궤적 방향: {path}");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }

    public static void ApplyToTrails()
    {
        FixTextures();
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/ACO_Addon/Prefabs" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) { continue; }
            ACO_Missile missile = asset.GetComponent<ACO_Missile>();
            ACO_Ordnance ordnance = asset.GetComponent<ACO_Ordnance>();
            if (missile == null && ordnance == null) { continue; }
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                ACO_Missile m = root.GetComponent<ACO_Missile>();
                ACO_Ordnance o = root.GetComponent<ACO_Ordnance>();
                StyleTrails(m != null ? m.TrailParticles : o.TrailParticles, m != null ? m.ExplosionDuration : o.ExplosionDuration);
                PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log($"[ACO] 연기 궤적: {path}");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        CreatePreviewPrefab();
    }

    /// 모든 미사일·폭탄·로켓·포탄 프리팹의 폭발을 바꾼다. 안의 소리와 폭발 범위 표시는 그대로다.
    public static void ApplyToWeapons()
    {
        FixTextures();
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/ACO_Addon/Prefabs" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null || (asset.GetComponent<ACO_Missile>() == null && asset.GetComponent<ACO_Ordnance>() == null)) { continue; }
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                StyleWeapon(root);
                PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log($"[ACO] 무장 폭발: {path}");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        CreatePreviewPrefab();
    }

    // ───────────── 유닛 파괴 ─────────────

    private const string PrefabFolder = "Assets/ACO_Addon/Prefabs";
    private const string BigBoom = "Assets/ACO_Addon/Audio/Generated/ACO_Explosion_Big.wav";
    private const string SmallBoom = "Assets/ACO_Addon/Audio/Generated/ACO_Explosion_Small.wav";
    private const string PlaneBoom = "Assets/SaccFlightAndVehicles/AssetFiles/SF-1/Sounds/PlaneExplode.wav";
    private const string DeathFxPrefix = "ACO_DeathFx_";
    private const float VoiceNear = 60f;
    private const float DefaultShipLength = 120f;
    private const float ShipSpread = 0.3f;      // 함선 불의 앞뒤 위치(길이 비율)
    private const float ShipForeDelay = 0.7f;
    private const float ShipAftDelay = 1.5f;

    private const int KindAir = 0;
    private const int KindGround = 1;
    private const int KindShip = 2;
    private const int KindBuilding = 3;
    private const int KindPart = 4;

    public static readonly string[] DeathFxNames = { DeathFxPrefix + "Air", DeathFxPrefix + "Ground", DeathFxPrefix + "Ship", DeathFxPrefix + "Building", DeathFxPrefix + "Part" };

    private static readonly string[] AirUnits = { "NPC_Fighter", "NPC_Attacker", "NPC_SuperAce", "NPC_Bomber", "NPC_Transport", "NPC_FriendlyGunship", "NPC_Helicopter" };
    private static readonly string[] ShipUnits = { "NPC_GuardShip", "NPC_Flagship", "NPC_AssaultShip", "NPC_Carrier", "NPC_Destroyer", "NPC_Warship" };
    private static readonly string[] BuildingUnits = { "NPC_Bunker", "NPC_Factory" };

    /// 파괴 때만 켜지는 자식. 안의 잔해 모델은 남기고 예전 파티클과 소리만 지운다.
    private static readonly string[] DeathRoots = { "Effects", "DeathEffects", "Explosion", "Rubble" };

    /// 효과를 넣을 ShowOnDeath 항목. 요새 부위의 Smoke처럼 그 밖의 것은 건드리지 않는다.
    private static readonly string[] DeathHolders = { "DeathEffects", "Effects", "Rubble" };

    // 아래 표의 순서: 공중, 지상, 함선, 건물, 부위. 처음 만들 때의 값이다 — 만든 뒤에는 효과 프리팹을 직접 고치면 전 유닛에 적용된다.
    private static readonly float[] BlastSize = { 1f, 1.5f, 3f, 3.2f, 0.6f };
    private static readonly float[] BlastLife = { 0.7f, 1f, 1.3f, 1.2f, 0.6f };
    private static readonly float[] BlastDust = { 0f, 1.2f, 0f, 2.6f, 0f };
    private static readonly float[] BlastSplash = { 0f, 0f, 2f, 0f, 0f };
    private static readonly int[] BurnStyle = { 0, 1, 2, 2, -1 };
    private static readonly float[] BurnTime = { 3.5f, 16f, 26f, 32f, 0f };
    private static readonly float[] AnchorHeight = { 0f, 2f, 14f, 10f, 0f };
    private static readonly string[] BoomClip = { PlaneBoom, SmallBoom, BigBoom, BigBoom, SmallBoom };
    private static readonly float[] BoomVolume = { 1f, 0.9f, 1f, 1f, 0.6f };
    private static readonly float[] BoomReach = { 5000f, 5000f, 9000f, 9000f, 3000f };

    // 불과 연기 모양: 공중(떨어지며 끄는 짧은 꼬리), 지상, 대형(함선·건물)
    private static readonly Vector2[] FlameSize = { new Vector2(8f, 14f), new Vector2(10f, 18f), new Vector2(30f, 55f) };
    private static readonly Vector2[] FlameLife = { new Vector2(0.5f, 0.9f), new Vector2(1f, 1.8f), new Vector2(1.5f, 2.5f) };
    private static readonly float[] FlameRate = { 14f, 5f, 5f };
    private static readonly Vector2[] PlumeSize = { new Vector2(10f, 22f), new Vector2(30f, 50f), new Vector2(70f, 110f) };
    private static readonly Vector2[] PlumeLife = { new Vector2(3f, 5f), new Vector2(12f, 18f), new Vector2(16f, 24f) };
    private static readonly Vector2[] PlumeSpeed = { new Vector2(1f, 4f), new Vector2(6f, 10f), new Vector2(8f, 14f) };
    private static readonly float[] PlumeRate = { 8f, 1.5f, 1.2f };

    /// 효과 프리팹을 만들고(이미 있으면 그대로 둔다) NPC 프리팹에서 스크립트가 파괴 때 켜는 오브젝트(ShowOnDeath)에 중첩한다. 예전 파티클과 소리는 지운다.
    public static void ApplyToDeath()
    {
        for (int kind = 0; kind < DeathFxNames.Length; kind++)
        {
            if (DeathFxAsset(kind) == null) { BuildDeathFx(kind); }
        }
        ACO_PrefabTuning.EditPrefabs(EditDeath);
    }

    /// 프리팹 생성 창: 다섯 종류를 처음 값으로 다시 만든다. GUID가 유지되므로 유닛의 중첩은 그대로다.
    public static GameObject[] CreateDeathFxPrefabs()
    {
        var built = new GameObject[DeathFxNames.Length + WreckFxNames.Length];
        for (int kind = 0; kind < DeathFxNames.Length; kind++) { built[kind] = BuildDeathFx(kind); }
        for (int i = 0; i < WreckFxNames.Length; i++) { built[DeathFxNames.Length + i] = BuildWreckFx(i); }
        return built;
    }

    /// 파괴 효과가 빠진 NPC 프리팹 수(NPC 프리팹을 다시 만들면 빠진다).
    public static int CountMissingDeathFx()
    {
        int missing = 0;
        foreach (string guid in AssetDatabase.FindAssets("NPC_ t:Prefab", new[] { PrefabFolder }))
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (asset == null || !asset.name.StartsWith("NPC_")) { continue; }
            bool boss = asset.GetComponentInChildren<ACO_FortressBlast>(true) != null;
            foreach (SaccTarget target in asset.GetComponentsInChildren<SaccTarget>(true))
            {
                if (boss && target.transform == asset.transform) { continue; }
                if (target.ExplodeOther == null || target.ExplodeOther.Length == 0) { continue; }
                if (FindDeathFx(DeathHolder(target, false)) == null) { missing++; break; }
            }
        }
        return missing;
    }

    /// 지상 파괴 효과 프리팹(없으면 만든다) — 남는 불·연기(Fx_Flame·Fx_Plume)를 끄면 한 번 터지는 폭발이 된다.
    public static GameObject GroundDeathFx()
    {
        GameObject asset = DeathFxAsset(KindGround);
        return asset != null ? asset : BuildDeathFx(KindGround);
    }

    private static GameObject DeathFxAsset(int kind)
    {
        return AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabFolder}/{DeathFxNames[kind]}.prefab");
    }

    private static Transform FindDeathFx(Transform effects)
    {
        if (effects == null) { return null; }
        foreach (Transform child in effects)
        {
            if (child.name.StartsWith(DeathFxPrefix)) { return child; }
        }
        return null;
    }

    /// 켜지는 순간 재생한다. 프리팹을 열고 루트를 고르면 Scene 뷰에서 전체를 재생해 볼 수 있다.
    private static GameObject BuildDeathFx(int kind)
    {
        FixTextures();
        var root = new GameObject(DeathFxNames[kind]);
        try
        {
            AddSilentParticle(root);
            AutoPlay(root.GetComponent<ParticleSystem>());
            if (kind == KindShip)
            {
                BuildDeathPoint(DeathPoint(root.transform, "Mid"), kind, 0f);
                BuildDeathPoint(DeathPoint(root.transform, "Fore"), kind, ShipForeDelay);
                BuildDeathPoint(DeathPoint(root.transform, "Aft"), kind, ShipAftDelay);
            }
            else { BuildDeathPoint(root.transform, kind, 0f); }
            BuildBoom(root.transform, kind);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabFolder}/{DeathFxNames[kind]}.prefab");
            Debug.Log($"[ACO] 파괴 효과 프리팹을 만들었습니다: {DeathFxNames[kind]}", prefab);
            return prefab;
        }
        finally { Object.DestroyImmediate(root); }
    }

    private static Transform DeathPoint(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    private static void BuildDeathPoint(Transform parent, int kind, float delay)
    {
        BuildCommon(parent, BlastSize[kind], BlastLife[kind]);
        if (BlastDust[kind] > 0f) { BuildGround(parent, BlastDust[kind], BlastLife[kind]); }
        if (BlastSplash[kind] > 0f) { BuildWater(parent, BlastSplash[kind], BlastLife[kind]); }
        if (BurnStyle[kind] >= 0) { BuildBurn(parent, BurnStyle[kind], BurnTime[kind]); }
        if (delay <= 0f) { return; }
        foreach (ParticleSystem ps in parent.GetComponentsInChildren<ParticleSystem>(true))
        {
            ParticleSystem.MainModule main = ps.main;
            main.startDelay = delay;
        }
    }

    /// 남는 불과 연기. time초 동안 나오고 멈춘다. 유닛을 따라 움직이고, 나온 연기는 그 자리에 남는다.
    private static void BuildBurn(Transform parent, int style, float time)
    {
        ParticleSystem flame = Part(parent, "Fx_Flame", FlameLife[style], new Vector2(2f, 7f), FlameSize[style], -0.02f, 40);
        Shape(flame, ParticleSystemShapeType.Cone, FlameSize[style].x * 0.3f, true);
        ParticleSystem.ShapeModule flameShape = flame.shape;
        flameShape.angle = 12f;
        Burn(flame, FlameRate[style], time);
        Flipbook(flame);
        Fade(flame, Color.white, Color.white, 1f, 0.1f);
        Grow(flame, 0.6f, 1.1f);
        Spin(flame, 0.35f);
        Draw(flame, Fire(), -10f).maxParticleSize = 8f;

        ParticleSystem plume = Part(parent, "Fx_Plume", PlumeLife[style], PlumeSpeed[style], PlumeSize[style], -0.01f, 48);
        Shape(plume, ParticleSystemShapeType.Cone, PlumeSize[style].x * 0.2f, true);
        ParticleSystem.ShapeModule plumeShape = plume.shape;
        plumeShape.angle = 8f;
        Burn(plume, PlumeRate[style], time);
        ParticleSystem.MainModule plumeMain = plume.main;
        plumeMain.startColor = new ParticleSystem.MinMaxGradient(new Color(0.09f, 0.085f, 0.08f), new Color(0.17f, 0.16f, 0.15f));
        Flipbook(plume);
        Fade(plume, new Color(1f, 0.66f, 0.42f), Color.white, 0.9f, 0.06f);
        Grow(plume, 0.5f, 2.4f);
        Spin(plume, 0.1f);
        Draw(plume, Smoke(), 10f).maxParticleSize = 8f;
    }

    private static void Burn(ParticleSystem ps, float rate, float time)
    {
        ParticleSystem.MainModule main = ps.main;
        main.loop = false;
        main.duration = time;
        main.playOnAwake = true;
        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
        ParticleSystem.EmissionModule emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = rate;
        emission.SetBursts(new ParticleSystem.Burst[0]);
    }

    private static void BuildBoom(Transform parent, int kind)
    {
        var go = new GameObject("Boom");
        go.transform.SetParent(parent, false);
        AudioSource voice = go.AddComponent<AudioSource>();
        voice.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(BoomClip[kind]);
        if (voice.clip == null) { Debug.LogWarning($"[ACO] 파괴 효과: 소리를 찾지 못했습니다: {BoomClip[kind]}"); }
        voice.playOnAwake = true;
        voice.loop = false;
        voice.volume = BoomVolume[kind];
        voice.spatialBlend = 1f;
        voice.dopplerLevel = 0f;
        voice.rolloffMode = AudioRolloffMode.Logarithmic;
        voice.minDistance = VoiceNear;
        voice.maxDistance = BoomReach[kind];
    }

    private static bool EditDeath(GameObject root)
    {
        bool boss = root.GetComponentInChildren<ACO_FortressBlast>(true) != null;
        bool any = false;
        foreach (SaccTarget target in root.GetComponentsInChildren<SaccTarget>(true))
        {
            if (PrefabUtility.IsPartOfPrefabInstance(target)) { continue; }   // 중첩 포탑은 자기 프리팹에서 고친다
            if (boss && target.transform == root.transform) { continue; }     // 요새와 주포의 대폭발은 그대로다
            foreach (string name in DeathRoots)
            {
                Transform death = target.transform.Find(name);
                if (death != null) { StripParticles(death); }
            }
            Transform holder = DeathHolder(target, true);
            if (holder == null) { continue; }   // 신호를 받는 스크립트가 없는 판정 전용(요새와 주포의 Hull)
            PlaceDeathFx(target, holder, DeathKind(root, target));
            any = true;
        }
        return any;
    }

    /// 파티클과 소리만 있는 자식을 지운다. 잔해 모델, 콜라이더, 스크립트, 중첩 프리팹이 있는 자식은 남기고 그 안을 살핀다.
    private static void StripParticles(Transform parent)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            Transform child = parent.GetChild(i);
            if (PrefabUtility.IsPartOfPrefabInstance(child.gameObject)) { continue; }
            if (IsEffectOnly(child)) { Object.DestroyImmediate(child.gameObject); }
            else { StripParticles(child); }
        }
    }

    private static bool IsEffectOnly(Transform t)
    {
        if (t.GetComponentsInChildren<ParticleSystem>(true).Length == 0 && t.GetComponentsInChildren<AudioSource>(true).Length == 0) { return false; }
        if (t.GetComponentsInChildren<Collider>(true).Length > 0 || t.GetComponentsInChildren<UdonSharpBehaviour>(true).Length > 0) { return false; }
        foreach (Renderer renderer in t.GetComponentsInChildren<Renderer>(true))
        {
            if (!(renderer is ParticleSystemRenderer)) { return false; }
        }
        foreach (Transform any in t.GetComponentsInChildren<Transform>(true))
        {
            if (PrefabUtility.IsPartOfPrefabInstance(any.gameObject)) { return false; }
        }
        return true;
    }

    private static int DeathKind(GameObject root, SaccTarget target)
    {
        if (target.transform != root.transform || root.name.StartsWith("NPC_Turret")) { return KindPart; }
        if (System.Array.IndexOf(AirUnits, root.name) >= 0) { return KindAir; }
        if (System.Array.IndexOf(ShipUnits, root.name) >= 0) { return KindShip; }
        if (System.Array.IndexOf(BuildingUnits, root.name) >= 0) { return KindBuilding; }
        return KindGround;
    }

    /// 파괴 신호를 받는 ACO 스크립트가 켜는 오브젝트. 없으면 create일 때 DeathEffects를 만들어 ShowOnDeath에 넣는다.
    private static Transform DeathHolder(SaccTarget target, bool create)
    {
        if (target.ExplodeOther == null) { return null; }
        foreach (UdonSharpBehaviour script in target.ExplodeOther)
        {
            if (script is ACO_TelUnit tel) { return tel.BlastEffect != null ? tel.BlastEffect.transform : null; }
            FieldInfo field = script != null ? script.GetType().GetField("ShowOnDeath") : null;
            if (field == null) { continue; }
            var shown = (GameObject[])field.GetValue(script) ?? new GameObject[0];
            foreach (GameObject go in shown)
            {
                if (go != null && System.Array.IndexOf(DeathHolders, go.name) >= 0) { return go.transform; }
            }
            if (!create) { return null; }
            Transform found = target.transform.Find("DeathEffects");
            if (found == null)
            {
                var made = new GameObject("DeathEffects");
                made.transform.SetParent(target.transform, false);
                made.SetActive(false);
                found = made.transform;
            }
            var list = new GameObject[shown.Length + 1];
            shown.CopyTo(list, 0);
            list[shown.Length] = found.gameObject;
            field.SetValue(script, list);
            EditorUtility.SetDirty(script);
            return found;
        }
        return null;
    }

    private static void PlaceDeathFx(SaccTarget target, Transform holder, int kind)
    {
        Transform placed = FindDeathFx(holder);
        if (placed == null) { placed = ((GameObject)PrefabUtility.InstantiatePrefab(DeathFxAsset(kind), holder)).transform; }
        placed.localPosition = Vector3.up * AnchorHeight[kind];
        placed.localRotation = Quaternion.identity;
        ACO_TelUnit tel = target.GetComponent<ACO_TelUnit>();
        if (tel != null)
        {
            tel.BlastTime = BurnTime[kind] + PlumeLife[BurnStyle[kind]].y;   // TEL은 이 시간 뒤에 폭발을 끈다 — 연기가 다 사라진 뒤로
            EditorUtility.SetDirty(tel);
        }
        if (kind != KindShip) { return; }
        float step = HullLength(target) * ShipSpread;
        placed.Find("Fore").localPosition = Vector3.forward * step;
        placed.Find("Aft").localPosition = Vector3.back * step;
    }

    private static float HullLength(SaccTarget target)
    {
        float length = 0f;
        foreach (BoxCollider box in target.GetComponents<BoxCollider>()) { length = Mathf.Max(length, box.size.z); }
        return length > 0f ? length : DefaultShipLength;
    }

    // ───────────── 불붙은 파편 ─────────────

    /// 격추되면 유닛이 멈춘 뒤 효과가 켜져 Inherit Velocity가 물려받을 속도가 없다. 그래서 진행 방향(+Z)으로 직접 쏜다 — 속도는 유닛의 비행 속도 범위다.
    public static readonly string[] WreckFxNames = { "ACO_WreckFx_Air", "ACO_WreckFx_Missile" };
    private const int WreckAir = 0;
    private const int WreckMissile = 1;
    private const string TelAirBlast = "AirBlast";
    private static readonly Vector2[] WreckSpeed = { new Vector2(130f, 280f), new Vector2(220f, 520f) };
    private static readonly Vector2[] WreckSize = { new Vector2(5f, 12f), new Vector2(6f, 14f) };
    private static readonly float[] WreckCount = { 14f, 18f };

    /// 파편 프리팹을 만들고(이미 있으면 그대로 둔다) 전투기·공격기·에이스와 TEL에 넣는다.
    public static void ApplyToWreck()
    {
        for (int i = 0; i < WreckFxNames.Length; i++)
        {
            if (WreckFxAsset(i) == null) { BuildWreckFx(i); }
        }
        ACO_PrefabTuning.EditPrefabs(EditWreck);
    }

    private static GameObject WreckFxAsset(int style)
    {
        return AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabFolder}/{WreckFxNames[style]}.prefab");
    }

    private static bool EditWreck(GameObject root)
    {
        if (root.GetComponent<ACO_NPCFighterAI>() != null)
        {
            SaccTarget target = root.GetComponent<SaccTarget>();
            Transform holder = target != null ? DeathHolder(target, false) : null;
            if (holder == null) { Debug.LogWarning($"[ACO] 파편: {root.name}에 파괴 효과가 없습니다. fx-death-1을 먼저 고치세요.", root); return false; }
            PlaceWreck(holder, WreckAir, root.transform.rotation);
            return true;
        }
        ACO_TelUnit tel = root.GetComponent<ACO_TelUnit>();
        if (tel == null) { return false; }
        Transform air = root.transform.Find(TelAirBlast);
        if (air == null)
        {
            var go = new GameObject(TelAirBlast);
            go.transform.SetParent(root.transform, false);
            go.SetActive(false);
            air = go.transform;
        }
        if (air.Find(DeathFxNames[KindAir]) == null) { PrefabUtility.InstantiatePrefab(DeathFxAsset(KindAir), air); }
        PlaceWreck(air, WreckMissile, Quaternion.LookRotation(Vector3.up, Vector3.forward));   // 탄도탄은 곧게 위로 올라간다
        tel.AirBlastEffect = air.gameObject;
        EditorUtility.SetDirty(tel);
        return true;
    }

    private static void PlaceWreck(Transform holder, int style, Quaternion rotation)
    {
        Transform placed = holder.Find(WreckFxNames[style]);
        if (placed == null) { placed = ((GameObject)PrefabUtility.InstantiatePrefab(WreckFxAsset(style), holder)).transform; }
        placed.localPosition = Vector3.zero;
        placed.rotation = rotation;
    }

    private static GameObject BuildWreckFx(int style)
    {
        FixTextures();
        var root = new GameObject(WreckFxNames[style]);
        try
        {
            AddSilentParticle(root);
            AutoPlay(root.GetComponent<ParticleSystem>());
            BuildWreckChunks(root.transform, style);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabFolder}/{WreckFxNames[style]}.prefab");
            Debug.Log($"[ACO] 파편 프리팹을 만들었습니다: {WreckFxNames[style]}", prefab);
            return prefab;
        }
        finally { Object.DestroyImmediate(root); }
    }

    /// 불붙은 덩어리: 앞으로 날아가며 느려지고 떨어진다. 짧은 불꼬리(Trails)와 뒤에 남는 연기(서브 이미터)를 끈다.
    private static void BuildWreckChunks(Transform parent, int style)
    {
        ParticleSystem chunk = Part(parent, "Fx_Chunk", new Vector2(2.2f, 3.8f), WreckSpeed[style], WreckSize[style], 0.5f, 24);
        Shape(chunk, ParticleSystemShapeType.Cone, 3f, false);
        ParticleSystem.ShapeModule shape = chunk.shape;
        shape.angle = 16f;
        SetBursts(chunk, 0.5f, 0f, WreckCount[style]);
        AutoPlay(chunk);
        Flipbook(chunk);
        Fade(chunk, Color.white, new Color(1f, 0.45f, 0.18f), 1f, 0f);
        Grow(chunk, 1f, 0.5f);
        Spin(chunk, 1.5f);
        ParticleSystem.LimitVelocityOverLifetimeModule drag = chunk.limitVelocityOverLifetime;
        drag.enabled = true;
        drag.limit = 25f;
        drag.dampen = 0.03f;
        ParticleSystem.TrailModule trail = chunk.trails;
        trail.enabled = true;
        trail.mode = ParticleSystemTrailMode.PerParticle;
        trail.ratio = 1f;
        trail.lifetime = 0.35f;
        trail.minVertexDistance = 4f;
        trail.dieWithParticles = false;
        trail.sizeAffectsWidth = true;
        trail.inheritParticleColor = false;
        trail.textureMode = ParticleSystemTrailTextureMode.Stretch;
        trail.widthOverTrail = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.1f));
        var heat = new Gradient();
        heat.SetKeys(
            new[] { new GradientColorKey(new Color(1f, 0.6f, 0.25f), 0f), new GradientColorKey(new Color(0.3f, 0.3f, 0.3f), 0.5f), new GradientColorKey(new Color(0.3f, 0.3f, 0.3f), 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.7f, 0.5f), new GradientAlphaKey(0f, 1f) });
        trail.colorOverTrail = new ParticleSystem.MinMaxGradient(heat);
        ParticleSystemRenderer renderer = Draw(chunk, Fire(), -5f);
        renderer.maxParticleSize = 8f;
        renderer.trailMaterial = Trail();

        ParticleSystem smoke = Part(chunk.transform, "Fx_ChunkSmoke", new Vector2(1.5f, 3f), new Vector2(1f, 5f), new Vector2(9f, 18f), -0.02f, 120);
        Shape(smoke, ParticleSystemShapeType.Sphere, 1f, false);
        ParticleSystem.MainModule smokeMain = smoke.main;
        smokeMain.loop = true;
        smokeMain.playOnAwake = false;
        smokeMain.startColor = new ParticleSystem.MinMaxGradient(new Color(0.13f, 0.125f, 0.12f), new Color(0.24f, 0.23f, 0.22f));
        ParticleSystem.EmissionModule emission = smoke.emission;
        emission.enabled = true;
        emission.rateOverTime = 6f;
        emission.SetBursts(new ParticleSystem.Burst[0]);
        Flipbook(smoke);
        Fade(smoke, new Color(1f, 0.66f, 0.42f), Color.white, 0.85f, 0.08f);
        Grow(smoke, 0.5f, 1.6f);
        Spin(smoke, 0.15f);
        Draw(smoke, Smoke(), 10f).maxParticleSize = 8f;
        ParticleSystem.SubEmittersModule sub = chunk.subEmitters;
        sub.enabled = true;
        while (sub.subEmittersCount > 0) { sub.RemoveSubEmitter(0); }
        sub.AddSubEmitter(smoke, ParticleSystemSubEmitterType.Birth, ParticleSystemSubEmitterProperties.InheritNothing);
    }

    // ───────────── 흙먼지(육상전함 궤도) ─────────────

    /// 궤도 흙먼지: 지상 폭발 흙먼지(BuildGround의 Fx_Dust)와 같은 연기 텍스처(ACO_FxSmoke 8x8 플립북)·흙색·부풀며 옅어지기.
    /// 방출량·수명·크기·모양은 호출하는 쪽 값을 쓴다.
    public static void StyleDust(ParticleSystem ps)
    {
        if (ps == null) { return; }
        FixTexture("fx_smoke_8x8.png", false);
        ParticleSystem.MainModule main = ps.main;
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.46f, 0.42f, 0.36f), new Color(0.62f, 0.57f, 0.5f));
        Flipbook(ps);
        Fade(ps, Color.white, Color.white, 0.75f, 0.08f);
        Grow(ps, 0.6f, 1.6f);
        Spin(ps, 0.15f);
        Draw(ps, Smoke(), 5f).maxParticleSize = 8f;
        EditorUtility.SetDirty(ps);
    }

    // ───────────── 함선 항적 ─────────────

    /// 크기에 맞춘 간격으로 거품 조각을 깐다 — 큰 배일수록 덜 뿌려도 빈틈이 없다. 파티클 크기는 프리팹 값을 쓴다.
    /// 선수는 둥글게 번지게 해서, 배가 앞으로 가면 자연스럽게 V자가 된다.
    public static void StyleWake(ACO_WaterFx fx)
    {
        if (fx == null || fx.Particles == null) { return; }
        foreach (ParticleSystem ps in fx.Particles)
        {
            if (ps == null) { continue; }
            bool bow = ps.gameObject.name == "BowWave";
            ParticleSystem.MainModule main = ps.main;
            float size = main.startSize.constantMax;
            main.maxParticles = 96;
            main.startColor = new Color(0.95f, 0.98f, 1f, bow ? 0.7f : 0.82f);
            main.startSpeed = bow ? new ParticleSystem.MinMaxCurve(size * 0.14f, size * 0.22f) : new ParticleSystem.MinMaxCurve(0f);
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.rateOverDistance = 1f / Mathf.Max(size * (bow ? 0.6f : 0.5f), 1f);
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            if (bow)
            {
                shape.shapeType = ParticleSystemShapeType.Circle;
                shape.radius = size * 0.2f;
                shape.radiusThickness = 0f;
                ps.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            }
            else { shape.radius = size * 0.25f; }
            NoFlipbook(ps);
            Fade(ps, Color.white, Color.white, 1f, 0.06f);
            Draw(ps, Foam(), 0f).renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            EditorUtility.SetDirty(ps);
        }
    }

    public static void ApplyToWakes()
    {
        FixTextures();
        ACO_PrefabTuning.EditPrefabs(root =>
        {
            bool any = false;
            foreach (ACO_WaterFx fx in root.GetComponentsInChildren<ACO_WaterFx>(true))
            {
                if (fx.Mode != ACO_WaterFx.MODE_WAKE || PrefabUtility.IsPartOfPrefabInstance(fx)) { continue; }
                StyleWake(fx);
                any = true;
            }
            return any;
        });
    }

    // ───────────── 레일건 ─────────────

    /// 소형 레일건 탄·공중 폭발과 주포·소형 레일건의 고리. 궤적의 고리는 서브 이미터가 거리 기준으로 남긴다:
    /// 소형은 탄 파티클에, 주포는 스크립트가 쏘는 보이지 않는 파티클(RingCarrier)에 달려 있다.
    public static void StyleRailgun(ACO_Railgun railgun)
    {
        if (railgun == null) { return; }
        if (railgun.MuzzleFlash)
        {
            NoFlipbook(railgun.MuzzleFlash);
            Fade(railgun.MuzzleFlash, Color.white, RailgunColor, 1f, 0f);
            Grow(railgun.MuzzleFlash, 0.6f, 1f);
            Draw(railgun.MuzzleFlash, Flash(), -30f).maxParticleSize = 8f;
        }
        railgun.RingCarrier = MainRingCarrier(railgun);
        ParticleSystem muzzleRing = Ring(railgun.transform, "Fx_MuzzleRing", new Vector2(2.2f, 2.8f));
        ParticleSystem.MainModule muzzleRingMain = muzzleRing.main;
        muzzleRingMain.startSize = MuzzleRingSize;
        railgun.MuzzleRing = muzzleRing;
        railgun.HorizonDelay = 2f;
        railgun.HorizonDistance = ImpactDistance;
        railgun.HorizonHeight = ImpactHeight;
        railgun.HorizonSoundDelay = 2.5f;
        SizeImpact(railgun.HorizonFlash, 14000f, 5000f);
        SizeImpact(railgun.HorizonGlow, 9000f, 2600f);
        EditorUtility.SetDirty(railgun);

        StyleMiniFx(railgun.GetComponentsInChildren<ACO_MiniRailgun>(true));
    }

    /// 소형 레일건 탄·공중 폭발 연출(랜스 포대·육상전함 주포). 첫 문의 ShellParticle 부모(MiniFx)에 불티·고리를 더하고 모든 문에 잇는다.
    public static void StyleMiniFx(ACO_MiniRailgun[] minis)
    {
        if (minis == null || minis.Length == 0 || minis[0].ShellParticle == null) { return; }
        ACO_MiniRailgun first = minis[0];
        Transform fx = first.ShellParticle.transform.parent;

        ParticleSystem shell = first.ShellParticle;
        ParticleSystem.MainModule shellMain = shell.main;
        shellMain.startColor = RailgunColor;
        ParticleSystem.ColorOverLifetimeModule shellColor = shell.colorOverLifetime;
        shellColor.enabled = false;   // 수명 = 비행 시간: 터질 때까지 밝게 간다
        ParticleSystemRenderer shellRenderer = Draw(shell, Tracer(), -25f);
        shellRenderer.renderMode = ParticleSystemRenderMode.Stretch;
        shellRenderer.lengthScale = 2f;
        shellRenderer.minParticleSize = 0.002f;
        shellRenderer.maxParticleSize = 8f;

        if (first.BurstParticle)
        {
            ParticleSystem.MainModule main = first.BurstParticle.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.8f, 3.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(120f, 260f);
            main.startColor = Color.white;
            Flipbook(first.BurstParticle);
            Fade(first.BurstParticle, Color.white, Color.white, 1f, 0f);
            Grow(first.BurstParticle, 0.55f, 1.15f);
            Spin(first.BurstParticle, 0.3f);
            Draw(first.BurstParticle, Fire(), -10f).maxParticleSize = 8f;
        }
        if (first.BurstFlash)
        {
            ParticleSystem.MainModule main = first.BurstFlash.main;
            main.startSize = 700f;
            Fade(first.BurstFlash, Color.white, RailgunColor, 1f, 0f);
            Grow(first.BurstFlash, 0.6f, 1f);
            Draw(first.BurstFlash, Flash(), -30f).maxParticleSize = 8f;
        }

        ParticleSystem sparks = Part(fx, "BurstSparks", new Vector2(0.8f, 2f), new Vector2(120f, 480f), new Vector2(8f, 20f), 0.3f, 90);
        Shape(sparks, ParticleSystemShapeType.Sphere, 60f, false);
        ParticleSystem.EmissionModule sparkEmission = sparks.emission;
        sparkEmission.rateOverTime = 0f;
        ParticleSystem.MainModule sparksMain = sparks.main;
        sparksMain.playOnAwake = false;
        sparksMain.loop = false;
        Fade(sparks, Color.white, RailgunColor, 1f, 0f);
        ParticleSystemRenderer sparksRenderer = Draw(sparks, Spark(), -20f);
        sparksRenderer.renderMode = ParticleSystemRenderMode.Stretch;
        sparksRenderer.velocityScale = 0.06f;
        sparksRenderer.lengthScale = 2f;
        sparksRenderer.minParticleSize = 0.001f;

        // 예전 판의 스크립트 방출용 고리
        Transform stale = fx.Find("Rings");
        if (stale != null) { Object.DestroyImmediate(stale.gameObject); }
        AttachRings(shell, new Vector2(0.9f, 1.4f), MiniRingSize, MiniRingSpacing, 40f / Mathf.Max(first.ShellSpeed, 1f), ParticleSystemSubEmitterProperties.InheritNothing);

        ParticleSystem burstRing = Ring(fx, "BurstRing", new Vector2(0.9f, 1.4f));
        foreach (ACO_MiniRailgun mini in minis)
        {
            mini.BurstRing = burstRing;
            mini.BurstSparks = sparks;
            PrefabUtility.RecordPrefabInstancePropertyModifications(mini);
            EditorUtility.SetDirty(mini);
        }
    }

    /// 착탄 빛: 15km일 때의 크기를 착탄 거리에 맞춰 키운다 — 화면에서는 같은 크기로 보인다.
    private static void SizeImpact(ParticleSystem ps, float width, float height)
    {
        if (ps == null) { return; }
        float k = ImpactDistance / 15000f;
        ParticleSystem.MainModule main = ps.main;
        main.startSizeX = width * k;
        main.startSizeY = height * k;
        main.startSizeZ = width * k;
    }

    /// 주포: 보이지 않는 파티클이 사선을 날아가며 고리를 남긴다. 크기를 줄여 가서 고리도 멀수록 작아진다(서브 이미터가 크기를 물려받는다).
    private static ParticleSystem MainRingCarrier(ACO_Railgun railgun)
    {
        float life = MainRingCount * MainRingSpacing / Mathf.Max(railgun.RingTravelSpeed, 1f);
        Transform stale = railgun.transform.Find("Fx_Rings");   // 예전 판의 스크립트 방출용 고리
        if (stale != null) { Object.DestroyImmediate(stale.gameObject); }
        ParticleSystem carrier = Part(railgun.transform, "Fx_RingCarrier", new Vector2(life, life), Vector2.zero, Vector2.one, 0f, 4);
        carrier.gameObject.layer = 0;
        ParticleSystem.MainModule main = carrier.main;
        main.playOnAwake = false;
        main.loop = false;
        ParticleSystem.EmissionModule emission = carrier.emission;
        emission.rateOverTime = 0f;
        ParticleSystem.ShapeModule shape = carrier.shape;
        shape.enabled = false;
        Grow(carrier, 1f, MainRingSizeFar / MainRingSize);
        carrier.GetComponent<ParticleSystemRenderer>().enabled = false;
        AttachRings(carrier, new Vector2(1.3f, 1.9f), MainRingSize, MainRingSpacing, 80f / Mathf.Max(railgun.RingTravelSpeed, 1f), ParticleSystemSubEmitterProperties.InheritSize);
        return carrier;
    }

    /// carrier가 지나간 거리마다 고리를 남기는 서브 이미터. drift = 부모 속도에서 물려받는 비율 — 고리가 부모 진행 방향을 보게 하는 값이라 0이면 안 된다.
    private static void AttachRings(ParticleSystem carrier, Vector2 life, float size, float spacing, float drift, ParticleSystemSubEmitterProperties inherit)
    {
        ParticleSystem rings = Ring(carrier.transform, "Rings", life);
        rings.transform.localPosition = Vector3.zero;
        rings.transform.localRotation = Quaternion.identity;
        ParticleSystem.MainModule main = rings.main;
        main.startSize = size;
        main.loop = true;
        ParticleSystem.EmissionModule emission = rings.emission;
        emission.rateOverTime = 0f;
        emission.rateOverDistance = 1f / Mathf.Max(spacing, 1f);
        ParticleSystem.InheritVelocityModule velocity = rings.inheritVelocity;
        velocity.enabled = true;
        velocity.mode = ParticleSystemInheritVelocityMode.Initial;
        velocity.curveMultiplier = drift;
        ParticleSystem.SubEmittersModule sub = carrier.subEmitters;
        sub.enabled = true;
        while (sub.subEmittersCount > 0) { sub.RemoveSubEmitter(0); }
        sub.AddSubEmitter(rings, ParticleSystemSubEmitterType.Birth, inherit);
    }

    /// 진행 방향을 향해 서는 고리: Velocity 정렬이라 속도가 방향을 정한다.
    private static ParticleSystem Ring(Transform parent, string name, Vector2 life)
    {
        ParticleSystem ps = Part(parent, name, life, Vector2.zero, new Vector2(100f, 100f), 0f, 64);
        ps.gameObject.layer = 0;
        ParticleSystem.MainModule main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.startRotation = 0f;
        ParticleSystem.EmissionModule emission = ps.emission;
        emission.rateOverTime = 0f;
        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = false;
        NoFlipbook(ps);
        Fade(ps, Color.white, RailgunColor, 0.95f, 0f);
        Grow(ps, 0.22f, 1f);
        ParticleSystemRenderer renderer = Draw(ps, Shock(), -28f);
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.alignment = ParticleSystemRenderSpace.Velocity;
        renderer.maxParticleSize = 8f;
        return ps;
    }

    public static void ApplyToRailgun()
    {
        FixTextures();
        GameObject root = PrefabUtility.LoadPrefabContents(RailgunPath);
        try
        {
            StyleRailgun(root.GetComponent<ACO_Railgun>());
            PrefabUtility.SaveAsPrefabAsset(root, RailgunPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        CreatePreviewPrefab();
    }

    // ───────────── 미리보기 프리팹 ─────────────

    /// 에디터에서 볼 프리팹. 씬에 넣지 않는다. NPC_Railgun의 실제 파티클을 복사하므로 값을 바꾸면 Create Prefabs에서 다시 만든다.
    /// 재생: 프리팹을 열고 묶음(Blast_Ground 등)을 고른 뒤 Scene 뷰 Particle Effect 패널의 Play/Restart.
    public static GameObject CreatePreviewPrefab()
    {
        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(RailgunPath);
        ACO_FortressBlast blast = source != null ? source.GetComponentInChildren<ACO_FortressBlast>(true) : null;
        if (blast == null)
        {
            Debug.LogWarning($"[ACO] 이펙트 미리보기: {RailgunPath}에서 대폭발을 찾지 못했습니다.");
            return null;
        }
        if (blast.Fire == null || !blast.Fire.textureSheetAnimation.enabled)
        {
            Debug.LogWarning("[ACO] 이펙트 미리보기: 대폭발이 아직 예전 모습입니다. Setup Check에서 fx-tracer-blast-1을 먼저 고치세요.");
            return null;
        }
        var root = new GameObject("ACO_FxPreview");
        AddSilentParticle(root);

        Transform ground = Group(root.transform, "Blast_Ground", Vector3.zero);
        CopyParticle(blast.Flash, ground);
        CopyParticle(blast.Shock, ground);
        CopyParticle(blast.Fire, ground);
        CopyParticle(blast.Debris, ground);
        CopyParticle(blast.Smoke, ground);

        Transform water = Group(root.transform, "Blast_Water", new Vector3(4000f, 0f, 0f));
        CopyParticle(blast.Flash, water);
        CopyParticle(blast.Shock, water);
        CopyParticle(blast.WaterColumn, water);
        CopyParticle(blast.Mist, water);

        Transform chain = Group(root.transform, "Chain_Secondary", new Vector3(-2000f, 0f, 0f));
        ParticleSystem secondary = CopyParticle(blast.Secondary, chain);
        if (secondary) { SetBursts(secondary, 2f, 0f, 10f, 0.5f, 10f, 1f, 10f); }

        Transform tracers = Group(root.transform, "Tracers", new Vector3(0f, 0f, -2000f));
        SampleTracer(tracers, "Enemy_Light", -30f, 25f, false);
        SampleTracer(tracers, "Enemy_Heavy", -10f, 4f, false);
        SampleTracer(tracers, "Friendly_Light", 10f, 25f, true);
        SampleTracer(tracers, "Friendly_Heavy", 30f, 4f, true);

        SampleWeapon(root.transform, "Missile", new Vector3(-600f, 0f, 2000f), 1f, 1f);
        SampleWeapon(root.transform, "HeavyMissile", new Vector3(0f, 0f, 3000f), 1.5f, 1f);
        SampleWeapon(root.transform, "Bomb", new Vector3(1200f, 0f, 2000f), 3.2f, 1f);
        SampleWeapon(root.transform, "Rocket", new Vector3(-1200f, 0f, 2000f), 0.6f, 0.49f);
        SampleRailgun(root.transform, source.GetComponent<ACO_Railgun>());
        SampleTrail(root.transform);

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PreviewPath);
        Object.DestroyImmediate(root);
        Debug.Log($"[ACO] 이펙트 미리보기를 만들었습니다: {PreviewPath} — 프리팹을 열고 Blast_Ground를 고른 뒤 Scene 뷰의 Particle Effect 패널에서 재생하세요.", prefab);
        return prefab;
    }

    /// 그리는 것이 없는 파티클: 이것을 고르면 Scene 뷰가 자식을 한꺼번에 재생한다.
    private static void AddSilentParticle(GameObject go)
    {
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = 30f;
        main.maxParticles = 1;
        ParticleSystem.EmissionModule emission = ps.emission;
        emission.enabled = false;
        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = false;
        go.GetComponent<ParticleSystemRenderer>().enabled = false;
    }

    private static Transform Group(Transform parent, string name, Vector3 position)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        AddSilentParticle(go);
        return go.transform;
    }

    /// 파티클 오브젝트만 복사한다. Udon 컴포넌트가 없는 자식이라 그대로 옮겨도 된다.
    private static ParticleSystem CopyParticle(ParticleSystem source, Transform parent)
    {
        if (source == null) { return null; }
        GameObject copy = Object.Instantiate(source.gameObject, parent);
        copy.name = source.gameObject.name;
        return copy.GetComponent<ParticleSystem>();
    }

    /// 재료 확인용: 에디터에서는 미사일을 움직일 수 없어 파티클이 곧게 날아가 리본을 그린다. 곡선과 실제 폭 변화는 게임에서 본다.
    private static void SampleTrail(Transform parent)
    {
        GameObject missile = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ACO_Addon/Prefabs/ACO_Missile_MRAAM.prefab");
        ACO_Missile script = missile != null ? missile.GetComponent<ACO_Missile>() : null;
        if (script == null || script.TrailParticles == null || script.TrailParticles.Length == 0) { return; }
        Transform group = Group(parent, "Missile_Trail", new Vector3(-2000f, 100f, 5000f));
        ParticleSystem trail = CopyParticle(script.TrailParticles[0], group);
        if (trail == null) { return; }
        ParticleSystem.MainModule main = trail.main;
        main.playOnAwake = false;
        main.startSpeed = 300f;
        main.startLifetime = 6f;
        Shape(trail, ParticleSystemShapeType.Cone, 0.01f, false);
        ParticleSystem.ShapeModule shape = trail.shape;
        shape.angle = 0f;
    }

    /// 스크립트가 쏘는 값을 Burst와 속도로 넣는다: 실제와 같은 서브 이미터가 도는 발사 2종 + 공중 폭발.
    private static void SampleRailgun(Transform parent, ACO_Railgun railgun)
    {
        if (railgun == null) { return; }
        ACO_MiniRailgun mini = railgun.GetComponentInChildren<ACO_MiniRailgun>(true);
        Transform mainShot = Group(parent, "Railgun_MainShot", new Vector3(-3000f, 200f, 5000f));
        ParticleSystem carrier = CopyParticle(railgun.RingCarrier, mainShot);
        if (carrier) { SampleShot(carrier, railgun.RingTravelSpeed); }
        ParticleSystem bigRing = CopyParticle(railgun.MuzzleRing, mainShot);
        if (bigRing) { SampleShot(bigRing, 80f); }
        Transform impact = Group(parent, "Railgun_Impact", new Vector3(-30000f, 0f, 5000f));
        ParticleSystem impactFlash = CopyParticle(railgun.HorizonFlash, impact);
        if (impactFlash) { SetBursts(impactFlash, 1f, 0f, 1f); }
        ParticleSystem impactGlow = CopyParticle(railgun.HorizonGlow, impact);
        if (impactGlow) { SetBursts(impactGlow, 1f, 0f, 1f); }
        if (mini == null) { return; }
        Transform shotGroup = Group(parent, "Railgun_MiniShot", new Vector3(0f, 200f, 5000f));
        ParticleSystem shell = CopyParticle(mini.ShellParticle, shotGroup);
        if (shell)
        {
            ParticleSystem.MainModule shellMain = shell.main;
            shellMain.startLifetime = 1.2f;
            SampleShot(shell, mini.ShellSpeed);
        }
        Transform burst = Group(parent, "Railgun_Burst", new Vector3(3000f, 300f, 5000f));
        ParticleSystem fire = CopyParticle(mini.BurstParticle, burst);
        if (fire) { SetBursts(fire, 0.5f, 0f, (float)mini.BurstCount); }
        ParticleSystem flash = CopyParticle(mini.BurstFlash, burst);
        if (flash) { SetBursts(flash, 0.5f, 0f, 1f); }
        ParticleSystem sparks = CopyParticle(mini.BurstSparks, burst);
        if (sparks) { SetBursts(sparks, 0.5f, 0f, (float)mini.BurstSparkCount); }
        ParticleSystem ring = CopyParticle(mini.BurstRing, burst);
        if (ring)
        {
            ParticleSystem.MainModule main = ring.main;
            main.startSpeed = 40f;
            main.startSize = mini.ZoneRadius * mini.BurstRingScale;
            Shape(ring, ParticleSystemShapeType.Cone, 0.01f, false);
            ParticleSystem.ShapeModule shape = ring.shape;
            shape.angle = 0f;
            SetBursts(ring, 0.5f, 0f, 1f);
        }
    }

    private static void SampleShot(ParticleSystem ps, float speed)
    {
        ParticleSystem.MainModule main = ps.main;
        main.startSpeed = speed;
        Shape(ps, ParticleSystemShapeType.Cone, 0.01f, false);
        ParticleSystem.ShapeModule shape = ps.shape;
        shape.angle = 0f;
        SetBursts(ps, 1f, 0f, 1f);
    }

    /// 등급 하나의 공중·땅·물 폭발을 나란히 둔다.
    private static void SampleWeapon(Transform parent, string name, Vector3 position, float k, float life)
    {
        Transform air = Group(parent, name + "_Air", position);
        BuildCommon(air, k, life);
        Transform ground = Group(parent, name + "_Ground", position + new Vector3(0f, 0f, 300f * k));
        BuildCommon(ground, k, life);
        BuildGround(ground, k, life);
        Transform water = Group(parent, name + "_Water", position + new Vector3(0f, 0f, 600f * k));
        BuildCommon(water, k, life);
        BuildWater(water, k, life);
    }

    private static void SampleTracer(Transform parent, string name, float x, float rate, bool friendly)
    {
        ParticleSystem ps = Child(parent, name, new Vector2(1.6f, 1.6f), new Vector2(900f, 900f), Vector2.one, 0f, 200);
        ps.transform.localPosition = new Vector3(x, 0f, 0f);
        ParticleSystem.MainModule main = ps.main;
        main.playOnAwake = false;
        main.loop = true;
        ParticleSystem.EmissionModule emission = ps.emission;
        emission.rateOverTime = rate;
        Shape(ps, ParticleSystemShapeType.Cone, 0.01f, false);
        ParticleSystem.ShapeModule shape = ps.shape;
        shape.angle = 0.6f;
        StyleTracer(ps, friendly);
    }
}
