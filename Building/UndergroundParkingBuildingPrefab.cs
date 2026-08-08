using System.Collections.Generic;
using UnityEngine;

namespace UndergroundParkingGarage
{
    internal static class UndergroundParkingBuildingPrefab
    {
        // Existing saves reference this generated kiosk prefab by its original
        // internal name. Player-facing localization uses the released name.
        public const string PrefabName = "Experimental Underground Parking Entrance";
        private const float SelectableHeight = 6f;
        private const float ForecourtPanelY = 0.15f;
        private const float ForecourtGlyphY = 0.19f;
        private const float SurfacePlinthBottomY = -0.45f;
        private const float SurfacePavingLift = 0.25f;
        private const int LocalEntertainmentAccumulation = 150;
        // Match the broad influence reach used by the vanilla decorative parking
        // structures. This is a local happiness effect only; visitor places remain 0.
        private const float LocalEntertainmentRadius = 400f;

        private static BuildingInfo _prefab;
        private static readonly BuildingInfo[] Prefabs =
            new BuildingInfo[UndergroundParkingStandaloneCatalog.VariantCount];
        private static readonly Material[] Materials =
            new Material[UndergroundParkingStandaloneCatalog.VariantCount];
        private static readonly Mesh[] ParkingMarkOverlayMeshes =
            new Mesh[UndergroundParkingStandaloneCatalog.VariantCount];
        private static Material _parkingMarkOverlayMaterial;

        public static BuildingInfo Prefab
        {
            get { return _prefab; }
        }

        internal static Mesh GetParkingMarkOverlayMesh(
            UndergroundParkingStandaloneVariant variant)
        {
            int index = (int)variant;
            if (index < 0 || index >= ParkingMarkOverlayMeshes.Length)
                index = 0;
            if (ParkingMarkOverlayMeshes[index] == null)
                ParkingMarkOverlayMeshes[index] = CreateParkingMarkOverlayMesh(
                    (UndergroundParkingStandaloneVariant)index);
            return ParkingMarkOverlayMeshes[index];
        }

        internal static Material ParkingMarkOverlayMaterial
        {
            get
            {
                if (_parkingMarkOverlayMaterial == null)
                {
                    Shader shader = Shader.Find("Hidden/Internal-Colored")
                                    ?? Shader.Find("Unlit/Color")
                                    ?? Shader.Find("UI/Default")
                                    ?? Shader.Find("Sprites/Default");
                    _parkingMarkOverlayMaterial = new Material(shader);
                    _parkingMarkOverlayMaterial.hideFlags = HideFlags.HideAndDontSave;
                    _parkingMarkOverlayMaterial.color = Color.white;
                    _parkingMarkOverlayMaterial.SetColor("_Color", Color.white);
                    _parkingMarkOverlayMaterial.SetInt("_SrcBlend",
                        (int)UnityEngine.Rendering.BlendMode.One);
                    _parkingMarkOverlayMaterial.SetInt("_DstBlend",
                        (int)UnityEngine.Rendering.BlendMode.Zero);
                    _parkingMarkOverlayMaterial.SetInt("_Cull",
                        (int)UnityEngine.Rendering.CullMode.Back);
                    _parkingMarkOverlayMaterial.SetInt("_ZWrite", 1);
                    _parkingMarkOverlayMaterial.SetInt("_ZTest",
                        (int)UnityEngine.Rendering.CompareFunction.LessEqual);
                    _parkingMarkOverlayMaterial.renderQueue = 2010;
                }

                return _parkingMarkOverlayMaterial;
            }
        }

        public static BuildingInfo EnsurePrefab()
        {
            return EnsurePrefab(UndergroundParkingStandaloneVariant.Compact);
        }

        public static BuildingInfo EnsurePrefab(UndergroundParkingStandaloneVariant variant)
        {
            UndergroundParkingLocalization.Apply();

            int variantIndex = (int)variant;
            if (variantIndex < 0 || variantIndex >= Prefabs.Length)
                variantIndex = 0;
            if (Prefabs[variantIndex] != null)
            {
                DisableSourceRenderer(Prefabs[variantIndex]);
                EnsureSelectableBounds();
                return Prefabs[variantIndex];
            }

            EnsureAllPrefabs();
            return Prefabs[variantIndex];
        }

        private static void EnsureAllPrefabs()
        {
            if (Prefabs[0] != null)
                return;

            BuildingInfo[] created = new BuildingInfo[
                UndergroundParkingStandaloneCatalog.VariantCount];
            for (int index = 0; index < created.Length; index++)
            {
                UndergroundParkingStandaloneSpec spec =
                    UndergroundParkingStandaloneCatalog.Get(
                        (UndergroundParkingStandaloneVariant)index);
                created[index] = CreatePrefab(spec);
                if (created[index] == null)
                    return;
            }

            if (!RegisterPrefabs(created))
            {
                UndergroundParkingLog.Error(
                    "Parking entrance prefab registration failed; placement will stay disabled.");
                return;
            }

            for (int index = 0; index < created.Length; index++)
            {
                Prefabs[index] = created[index];
                DisableSourceRenderer(created[index]);
                ApplySelectableBounds(created[index]);
                UndergroundParkingLog.Advanced(
                    "Runtime parking entrance building prefab ready: prefab="
                    + created[index].name
                    + " cells="
                    + created[index].m_cellWidth
                    + "x"
                    + created[index].m_cellLength
                    + " prefabIndex="
                    + created[index].m_prefabDataIndex);
            }

            _prefab = Prefabs[0];
            UndergroundParkingLocalization.Apply();
        }

        private static BuildingInfo CreatePrefab(UndergroundParkingStandaloneSpec spec)
        {
            GameObject gameObject = new GameObject(spec.PrefabName);
            Object.DontDestroyOnLoad(gameObject);

            BuildingInfo info = gameObject.AddComponent<BuildingInfo>();
            UndergroundParkingBuildingAI ai = gameObject.AddComponent<UndergroundParkingBuildingAI>();

            Mesh mesh = CreateEntranceMesh(spec);
            Material material = CreateEntranceMaterial();
            Materials[(int)spec.Variant] = material;

            MeshFilter filter = gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            MeshRenderer renderer = gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;

            info.name = spec.PrefabName;
            info.m_class = CreateItemClass();
            // Placement is owned exclusively by UPG's compact P tab. Keeping
            // this runtime prefab out of every vanilla/editor availability
            // context prevents GeneratedGroupPanel from creating a second
            // default Public Transport category for it.
            info.m_availableIn = ItemClass.Availability.None;
            info.m_placementMode = BuildingInfo.PlacementMode.Roadside;
            info.m_zoningMode = BuildingInfo.ZoningMode.NotZoning;
            info.m_cellWidth = spec.CellWidth;
            info.m_cellLength = spec.CellLength;
            info.m_placementOffset = 0f;
            info.m_flattenTerrain = true;
            info.m_flattenFullArea = false;
            info.m_fullPavement = true;
            info.m_clipTerrain = false;
            info.m_weakTerrainRuining = true;
            info.m_autoRemove = false;
            info.m_circular = false;
            info.m_useColorVariations = false;
            info.m_props = new BuildingInfo.Prop[0];
            info.m_subMeshes = new BuildingInfo.MeshInfo[0];
            info.m_subBuildings = new BuildingInfo.SubInfo[0];
            info.m_paths = new BuildingInfo.PathInfo[0];
            info.m_buildingAI = ai;
            info.m_mesh = mesh;
            info.m_material = material;
            DisableLod(info);
            info.m_generatedInfo = ScriptableObject.CreateInstance<BuildingInfoGen>();
            info.m_generatedInfo.name = spec.PrefabName + " Generated Info";
            info.m_generatedInfo.m_buildingInfo = info;
            info.m_color0 = new Color(0.55f, 0.62f, 0.68f);
            info.m_color1 = new Color(0.55f, 0.62f, 0.68f);
            info.m_color2 = new Color(0.55f, 0.62f, 0.68f);
            info.m_color3 = new Color(0.55f, 0.62f, 0.68f);

            ai.m_info = info;
            ai.m_constructionCost = spec.ConstructionCost;
            // ParkAI exposes 16 percent of this raw asset value as weekly upkeep.
            // 938 therefore displays as about 150/week, the one-floor equivalent
            // of the design formula: 400/month fixed + 200/month/floor.
            ai.m_maintenanceCost = 938;
            ai.m_electricityConsumption = 16;
            ai.m_waterConsumption = 0;
            ai.m_sewageAccumulation = 0;
            ai.m_garbageAccumulation = 0;
            ai.m_fireHazard = 0;
            ai.m_fireTolerance = 100;
            ai.m_entertainmentAccumulation = LocalEntertainmentAccumulation;
            ai.m_entertainmentRadius = LocalEntertainmentRadius;
            ai.m_visitPlaceCount0 = 0;
            ai.m_visitPlaceCount1 = 0;
            ai.m_visitPlaceCount2 = 0;

            info.CalculateGeneratedInfo();
            DisableLod(info);
            ApplySelectableBounds(info);
            return info;
        }

        private static void DisableSourceRenderer(BuildingInfo info)
        {
            if (info == null)
                return;

            // PrefabCollection and BuildingManager render placed/previewed
            // buildings from BuildingInfo.m_mesh and m_material. The retained
            // source GameObject itself must not remain an ordinary scene
            // renderer at world origin, where third-party preview cameras can
            // accidentally capture it alongside the requested asset.
            MeshRenderer renderer = info.GetComponent<MeshRenderer>();
            if (renderer == null)
                return;

            renderer.enabled = false;
            renderer.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private static void DisableLod(BuildingInfo info)
        {
            if (info == null)
                return;

            info.m_lodObject = null;
            info.m_lodMesh = null;
            info.m_lodMaterial = null;
            info.m_lodMeshData = null;
            info.m_lodMeshBase = null;
            info.m_lodMeshCombined1 = null;
            info.m_lodMeshCombined4 = null;
            info.m_lodMeshCombined8 = null;
            info.m_lodMaterialCombined = null;
            info.m_lodLocations = null;
            info.m_lodStates = null;
            info.m_lodObjectIndices = null;
            info.m_lodColors = null;
            info.m_lodCount = 0;
            info.m_lodMissing = true;

        }

        public static bool IsRegistered
        {
            get
            {
                for (int index = 0; index < Prefabs.Length; index++)
                {
                    if (!IsRegisteredPrefab(Prefabs[index]))
                        return false;
                }
                return true;
            }
        }

        public static bool IsGaragePrefab(BuildingInfo info)
        {
            if (info == null)
                return false;

            for (int index = 0; index < Prefabs.Length; index++)
            {
                UndergroundParkingStandaloneSpec spec =
                    UndergroundParkingStandaloneCatalog.Get(
                        (UndergroundParkingStandaloneVariant)index);
                if (info == Prefabs[index] || info.name == spec.PrefabName)
                    return true;
            }
            return false;
        }

        public static UndergroundParkingStandaloneVariant GetVariant(BuildingInfo info)
        {
            if (info != null)
            {
                for (int index = 0; index < Prefabs.Length; index++)
                {
                    UndergroundParkingStandaloneSpec spec =
                        UndergroundParkingStandaloneCatalog.Get(
                            (UndergroundParkingStandaloneVariant)index);
                    if (info == Prefabs[index] || info.name == spec.PrefabName)
                        return spec.Variant;
                }
            }
            return UndergroundParkingStandaloneVariant.Compact;
        }

        public static bool IsGarageBuilding(ushort buildingId)
        {
            BuildingManager manager = BuildingManager.instance;
            if (manager == null
                || buildingId == 0
                || buildingId >= manager.m_buildings.m_size)
            {
                return false;
            }

            Building building = manager.m_buildings.m_buffer[buildingId];
            if ((building.m_flags & Building.Flags.Created) == 0
                || (building.m_flags & Building.Flags.Deleted) != 0)
            {
                return false;
            }

            return IsGaragePrefab(building.Info);
        }

        public static void EnsureSelectableBounds()
        {
            for (int index = 0; index < Prefabs.Length; index++)
                ApplySelectableBounds(Prefabs[index]);
        }

        internal static void UpdateWeatherResponse(float wetness)
        {
            for (int index = 0; index < Materials.Length; index++)
                ApplyWeatherResponse(Materials[index], wetness);
        }

        private static void ApplyWeatherResponse(Material material, float wetness)
        {
            if (material == null)
                return;

            wetness = Mathf.Clamp01(wetness);
            Color tint = Color.Lerp(Color.white, new Color(0.82f, 0.84f, 0.86f, 1f), wetness);
            material.color = tint;
            material.SetColor("_Color", tint);
            if (material.HasProperty("_Metallic"))
                material.SetFloat("_Metallic", 0f);
            if (material.HasProperty("_Glossiness"))
                material.SetFloat("_Glossiness", Mathf.Lerp(0.10f, 0.72f, wetness));
            if (material.HasProperty("_GlossMapScale"))
                material.SetFloat("_GlossMapScale", Mathf.Lerp(0.10f, 0.72f, wetness));
        }

        private static float GetCurrentSurfaceWetness()
        {
            WeatherManager weather = WeatherManager.instance;
            return weather == null
                ? 0f
                : Mathf.Clamp01(Mathf.Max(
                    weather.m_groundWetness,
                    weather.m_currentRain * 0.75f));
        }

        public static void RefreshBuildingSelection(ushort buildingId)
        {
            if (!IsGarageBuilding(buildingId))
                return;

            EnsureSelectableBounds();

            BuildingManager manager = BuildingManager.instance;
            if (manager == null)
                return;

            manager.UpdateBuilding(buildingId);
            manager.UpdateBuildingRenderer(buildingId, true);
        }

        public static void Release()
        {
            // Runtime prefabs stay registered with PrefabCollection for the process lifetime.
        }

        private static void ApplySelectableBounds(BuildingInfo info)
        {
            if (info == null)
                return;

            float footprintWidth = info.m_cellWidth * 8f;
            float footprintLength = info.m_cellLength * 8f;
            Vector3 size = new Vector3(footprintWidth, SelectableHeight, footprintLength);
            info.m_size = size;
            info.m_centerOffset = Vector3.up * (SelectableHeight * 0.5f);
            info.m_collisionHeight = SelectableHeight;
            info.m_renderSize = Mathf.Max(footprintWidth, footprintLength);

            if (info.m_generatedInfo == null)
                return;

            info.m_generatedInfo.m_min = new Vector3(-footprintWidth * 0.5f, 0f, -footprintLength * 0.5f);
            info.m_generatedInfo.m_max = new Vector3(footprintWidth * 0.5f, SelectableHeight, footprintLength * 0.5f);
            info.m_generatedInfo.m_size = size;
        }

        private static bool RegisterPrefabs(BuildingInfo[] infos)
        {
            try
            {
                PrefabCollection<BuildingInfo>.InitializePrefabs(
                    "UndergroundParkingGarage",
                    infos,
                    null);
                PrefabCollection<BuildingInfo>.BindPrefabs();
            }
            catch (System.Exception e)
            {
                UndergroundParkingLog.Error("Parking entrance prefab registration failed: " + e);
                return false;
            }

            for (int index = 0; index < infos.Length; index++)
            {
                if (!IsRegisteredPrefab(infos[index]))
                    return false;
            }
            return true;
        }

        private static bool IsRegisteredPrefab(BuildingInfo info)
        {
            if (info == null || info.m_prefabDataIndex < 0)
                return false;

            try
            {
                BuildingInfo loaded = PrefabCollection<BuildingInfo>.GetPrefab((uint)info.m_prefabDataIndex);
                return loaded == info;
            }
            catch
            {
                return false;
            }
        }

        private static ItemClass CreateItemClass()
        {
            ItemClass itemClass = ScriptableObject.CreateInstance<ItemClass>();
            itemClass.name = "Underground Parking Garage Class";
            itemClass.m_service = ItemClass.Service.PublicTransport;
            itemClass.m_subService = ItemClass.SubService.PublicTransportMetro;
            itemClass.m_level = ItemClass.Level.Level1;
            itemClass.m_layer = ItemClass.Layer.Default
                                | ItemClass.Layer.PublicTransport
                                | ItemClass.Layer.MetroTunnels;
            return itemClass;
        }

        private static Material CreateEntranceMaterial()
        {
            Shader shader = Shader.Find("Standard")
                            ?? Shader.Find("Diffuse")
                            ?? Shader.Find("Legacy Shaders/Diffuse")
                            ?? Shader.Find("Unlit/Texture");
            Material material = new Material(shader);
            material.hideFlags = HideFlags.HideAndDontSave;
            material.mainTexture = CreatePaletteTexture();
            material.color = Color.white;
            material.SetColor("_Color", Color.white);
            if (material.HasProperty("_Metallic"))
                material.SetFloat("_Metallic", 0f);
            if (material.HasProperty("_Glossiness"))
                material.SetFloat("_Glossiness", 0.10f);
            ApplyWeatherResponse(material, GetCurrentSurfaceWetness());
            return material;
        }

        private static Texture2D CreatePaletteTexture()
        {
            const int size = 256;
            Texture2D texture = new Texture2D(size, size, TextureFormat.ARGB32, true);
            texture.hideFlags = HideFlags.HideAndDontSave;
            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                    pixels[y * size + x] = GetSurfaceAtlasPixel(x, y);
            }

            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.anisoLevel = 4;
            return texture;
        }

        private static Color32 GetSurfaceAtlasPixel(int x, int y)
        {
            int noise = ((x * 37 + y * 71 + x * y * 3) & 15) - 7;
            if (y < 128 && x < 128)
            {
                int aggregate = ((x * 13 + y * 29) % 53 == 0) ? 18 : 0;
                return Shade(new Color32(58, 63, 66, 255), noise + aggregate);
            }

            if (y < 128)
            {
                bool joint = ((x - 128) % 32 <= 1) || (y % 24 <= 1);
                return joint
                    ? new Color32(82, 88, 91, 255)
                    : Shade(new Color32(145, 149, 148, 255), noise / 3);
            }

            if (y < 192 && x < 128)
            {
                bool grain = x % 24 <= 1;
                return grain
                    ? new Color32(69, 48, 35, 255)
                    : Shade(new Color32(118, 82, 55, 255), noise / 3);
            }

            if (y < 192)
                return new Color32(0, 102, 178, 255);

            if (x < 128)
            {
                bool highlight = x % 18 == 0;
                return highlight ? new Color32(78, 84, 88, 255) : Shade(new Color32(31, 35, 38, 255), noise / 4);
            }

            return new Color32(254, 254, 254, 255);
        }

        private static Color32 Shade(Color32 color, int delta)
        {
            return new Color32(
                (byte)Mathf.Clamp(color.r + delta, 0, 255),
                (byte)Mathf.Clamp(color.g + delta, 0, 255),
                (byte)Mathf.Clamp(color.b + delta, 0, 255),
                color.a);
        }

        private static Mesh CreateEntranceMesh(UndergroundParkingStandaloneSpec spec)
        {
            MeshDraft draft = new MeshDraft();
            float halfWidth = spec.CellWidth * 4f;
            float halfLength = spec.CellLength * 4f;
            float surfaceHalfWidth =
                spec.Variant == UndergroundParkingStandaloneVariant.Compact
                    ? halfWidth
                    : spec.Variant == UndergroundParkingStandaloneVariant.Square
                        ? 4f
                        : 8f;
            float surfaceHalfLength =
                spec.Variant == UndergroundParkingStandaloneVariant.Compact
                    ? halfLength
                    : 12f;
            float surfaceLift = GetSurfacePavingLift(spec.Variant);
            Vector2 tarmac = Uv(0.15f, 0.15f);
            Vector2 concrete = Uv(0.65f, 0.15f);
            Vector2 bronze = Uv(0.15f, 0.65f);
            Vector2 blue = Uv(0.65f, 0.65f);
            Vector2 dark = Uv(0.15f, 0.9f);
            Vector2 white = Uv(0.9f, 0.9f);

            // The building footprint remains authoritative for placement,
            // selection, bulldozing and zoning removal. Larger variants expose
            // live terrain outside a grid-aligned paved entrance. Civic uses
            // the exact central 8m column of its odd 3x3 footprint; Compact and
            // Grand use centred even-cell pads. Vanilla can level only the
            // prefab's full generated bounds, so the localized pad is a shallow
            // raised plinth with below-grade sides instead of a terrain-clipped
            // single plane. This preserves live terrain outside Civic and Grand.
            AddBox(
                draft,
                -surfaceHalfWidth,
                surfaceHalfWidth,
                SurfacePlinthBottomY,
                0.03f + surfaceLift,
                -surfaceHalfLength,
                surfaceHalfLength,
                tarmac);
            AddHorizontalQuad(draft, -3.25f, 3.25f, -10.95f, -3.55f, 0.08f + surfaceLift, new Rect(0.02f, 0.02f, 0.46f, 0.46f));
            AddHorizontalQuad(draft, -2.4f, 2.4f, -3.55f, 7.5f, 0.09f + surfaceLift, new Rect(0.02f, 0.02f, 0.46f, 0.46f));
            AddBox(draft, -surfaceHalfWidth, -surfaceHalfWidth + 0.65f, SurfacePlinthBottomY, 0.18f + surfaceLift, -surfaceHalfLength, surfaceHalfLength, concrete);
            AddBox(draft, surfaceHalfWidth - 0.65f, surfaceHalfWidth, SurfacePlinthBottomY, 0.18f + surfaceLift, -surfaceHalfLength, surfaceHalfLength, concrete);
            AddBox(draft, -surfaceHalfWidth + 0.65f, surfaceHalfWidth - 0.65f, SurfacePlinthBottomY, 0.18f + surfaceLift, surfaceHalfLength - 0.65f, surfaceHalfLength, concrete);
            AddForecourtParkingMark(
                draft,
                GetForecourtParkingMarkZOffset(spec.Variant),
                surfaceLift);

            // Dark portal recess and a continuous ramp make the entrance read as
            // genuinely descending even though the surface building changes no terrain.
            AddVerticalQuad(draft, -3.25f, 3.25f, 0.08f, 3.35f, -10.9f, dark);
            AddHorizontalQuad(draft, -3.02f, -2.91f, -10.75f, -3.72f, 0.125f, white);
            AddHorizontalQuad(draft, 2.91f, 3.02f, -10.75f, -3.72f, 0.125f, white);

            // Deep concrete side walls frame a clear 6.7-metre vehicle opening.
            AddBox(draft, -4.25f, -3.35f, 0.1f, 3.35f, -10.8f, -3.65f, concrete);
            AddBox(draft, 3.35f, 4.25f, 0.1f, 3.35f, -10.8f, -3.65f, concrete);
            AddBox(draft, -4.42f, -3.22f, 0.08f, 0.34f, -10.95f, -3.48f, concrete);
            AddBox(draft, 3.22f, 4.42f, 0.08f, 0.34f, -10.95f, -3.48f, concrete);

            if (spec.Variant == UndergroundParkingStandaloneVariant.Grand)
            {
                AddGrandRibbonArchitecture(
                    draft,
                    concrete,
                    bronze,
                    blue,
                    dark,
                    white);
            }
            else if (spec.Variant == UndergroundParkingStandaloneVariant.Square)
            {
                AddCivicLanternArchitecture(
                    draft,
                    concrete,
                    bronze,
                    blue,
                    dark,
                    white);
            }
            else
            {
                AddCompactPortalArchitecture(
                    draft,
                    bronze,
                    dark);
            }

            // An integrated, road-facing parking blade replaces the old roof box.
            AddVerticalQuad(draft, -0.72f, 0.72f, 3.62f, 5.06f, -3.44f, blue);
            UndergroundParkingMarkGeometry.AddCenteredParkingSignPVertical(
                draft.Vertices,
                draft.Uvs,
                null,
                draft.Triangles,
                0f,
                4.34f,
                0.92f,
                1f,
                1f,
                -3.42f,
                white);

            // Maintained roadside details: a transverse drainage grate and
            // four low protective stainless bollards around the ramp mouth.
            AddHorizontalQuad(draft, -3.18f, 3.18f, -3.58f, -3.32f, 0.135f, dark);
            for (int grate = -5; grate <= 5; grate++)
            {
                float x = grate * 0.54f;
                AddHorizontalQuad(draft, x - 0.025f, x + 0.025f, -3.6f, -3.3f, 0.142f, white);
            }

            AddBox(draft, -3.52f, -3.24f, 0.1f, 1.05f, -3.58f, -3.3f, white);
            AddBox(draft, 3.24f, 3.52f, 0.1f, 1.05f, -3.58f, -3.3f, white);
            AddBox(draft, -3.02f, -2.74f, 0.1f, 1.05f, 0.95f, 1.23f, white);
            AddBox(draft, 2.74f, 3.02f, 0.1f, 1.05f, 0.95f, 1.23f, white);

            Mesh mesh = new Mesh();
            mesh.name = spec.Title + " Mesh";
            mesh.vertices = draft.Vertices.ToArray();
            mesh.uv = draft.Uvs.ToArray();
            mesh.triangles = draft.Triangles.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void AddCompactPortalArchitecture(
            MeshDraft draft,
            Vector2 bronze,
            Vector2 dark)
        {
            // The released compact entrance retains its accepted charcoal frame,
            // warm slatted canopy and open roadside sightlines.
            AddBox(draft, -4.45f, 4.45f, 3.25f, 3.75f, -4.08f, -3.48f, dark);
            AddBox(draft, -4.35f, -3.88f, 3.28f, 3.62f, -10.75f, -3.62f, dark);
            AddBox(draft, 3.88f, 4.35f, 3.28f, 3.62f, -10.75f, -3.62f, dark);
            AddBox(draft, -4.2f, 4.2f, 3.35f, 3.65f, -10.78f, -10.3f, dark);
            for (int slat = 0; slat < 8; slat++)
            {
                float z = -9.9f + slat * 0.78f;
                AddBox(
                    draft,
                    -4.12f,
                    4.12f,
                    3.54f,
                    3.76f,
                    z - 0.12f,
                    z + 0.12f,
                    bronze);
            }

            for (int fin = 0; fin < 4; fin++)
            {
                float z = -9.55f + fin * 1.55f;
                AddBox(
                    draft,
                    -4.48f,
                    -4.18f,
                    0.22f,
                    3.45f,
                    z - 0.16f,
                    z + 0.16f,
                    bronze);
                AddBox(
                    draft,
                    4.18f,
                    4.48f,
                    0.22f,
                    3.45f,
                    z - 0.16f,
                    z + 0.16f,
                    bronze);
            }
        }

        private static void AddCivicLanternArchitecture(
            MeshDraft draft,
            Vector2 concrete,
            Vector2 bronze,
            Vector2 blue,
            Vector2 dark,
            Vector2 white)
        {
            // The 3x3 Civic pavilion is a compact folded gateway: five white
            // structural ribs rise into a warm pitched canopy and a blue
            // skylight spine. Its tall silhouette reads as public architecture
            // rather than a stretched version of the compact car-port.
            const float frontZ = -10.35f;
            const float backZ = -3.55f;
            const float shoulderX = 4.9f;
            const float shoulderY = 4.05f;
            const float ridgeY = 5.55f;

            for (int frame = 0; frame < 5; frame++)
            {
                float z = frontZ + frame * ((backZ - frontZ) / 4f);
                AddOrientedBeam(
                    draft,
                    new Vector3(-5.75f, -1.8f, z),
                    new Vector3(-shoulderX, shoulderY, z),
                    0.34f,
                    white);
                AddOrientedBeam(
                    draft,
                    new Vector3(5.75f, -1.8f, z),
                    new Vector3(shoulderX, shoulderY, z),
                    0.34f,
                    white);
                AddOrientedBeam(
                    draft,
                    new Vector3(-shoulderX, shoulderY, z),
                    new Vector3(0f, ridgeY, z),
                    0.30f,
                    white);
                AddOrientedBeam(
                    draft,
                    new Vector3(shoulderX, shoulderY, z),
                    new Vector3(0f, ridgeY, z),
                    0.30f,
                    white);
            }

            AddDoubleSidedQuad(
                draft,
                new Vector3(-shoulderX, shoulderY - 0.12f, frontZ),
                new Vector3(0f, ridgeY - 0.12f, frontZ),
                new Vector3(0f, ridgeY - 0.12f, backZ),
                new Vector3(-shoulderX, shoulderY - 0.12f, backZ),
                bronze);
            AddDoubleSidedQuad(
                draft,
                new Vector3(0f, ridgeY - 0.12f, frontZ),
                new Vector3(shoulderX, shoulderY - 0.12f, frontZ),
                new Vector3(shoulderX, shoulderY - 0.12f, backZ),
                new Vector3(0f, ridgeY - 0.12f, backZ),
                concrete);

            AddBox(
                draft,
                -0.42f,
                0.42f,
                ridgeY - 0.02f,
                ridgeY + 0.18f,
                frontZ - 0.08f,
                backZ + 0.08f,
                blue);

            // Open, diagonal side braces make the pavilion change as the player
            // orbits it while keeping the vehicle corridor unobstructed.
            AddOrientedBeam(
                draft,
                new Vector3(-5.72f, -1.8f, frontZ + 0.4f),
                new Vector3(-4.92f, 4.25f, backZ - 0.25f),
                0.22f,
                dark);
            AddOrientedBeam(
                draft,
                new Vector3(5.72f, -1.8f, backZ - 0.25f),
                new Vector3(4.92f, 4.25f, frontZ + 0.4f),
                0.22f,
                dark);
        }

        private static void AddGrandRibbonArchitecture(
            MeshDraft draft,
            Vector2 concrete,
            Vector2 bronze,
            Vector2 blue,
            Vector2 dark,
            Vector2 white)
        {
            // The 4x4 Grand pavilion is a broad asymmetric ribbon. Folded roof
            // bands rise to an offset spine, producing a different profile from
            // every direction and a landmark scale appropriate to the Grand
            // garage's two-floor capacity.
            const int sectionCount = 9;
            Vector3[] left = new Vector3[sectionCount];
            Vector3[] spine = new Vector3[sectionCount];
            Vector3[] right = new Vector3[sectionCount];
            for (int section = 0; section < sectionCount; section++)
            {
                float progress = section / (float)(sectionCount - 1);
                float z = Mathf.Lerp(-10.45f, 6.2f, progress);
                float wave = Mathf.Sin(progress * Mathf.PI) * 1.15f;
                float sweep = Mathf.Sin(progress * Mathf.PI * 1.5f) * 0.85f;
                left[section] = new Vector3(
                    -14.25f,
                    3.35f + wave * 0.28f,
                    z - 0.45f);
                spine[section] = new Vector3(
                    -1.7f + sweep,
                    4.4f + wave,
                    z + 0.75f);
                right[section] = new Vector3(
                    14.25f,
                    4.05f + wave * 0.42f,
                    z + 0.2f);

                Vector2 ribColor = section % 2 == 0 ? white : dark;
                AddOrientedBeam(
                    draft,
                    left[section],
                    spine[section],
                    0.28f,
                    ribColor);
                AddOrientedBeam(
                    draft,
                    spine[section],
                    right[section],
                    0.28f,
                    ribColor);
            }

            for (int section = 0; section < sectionCount - 1; section++)
            {
                Vector2 leftColor = section % 3 == 1 ? concrete : bronze;
                Vector2 rightColor = section % 3 == 1 ? bronze : concrete;
                AddDoubleSidedQuad(
                    draft,
                    left[section],
                    spine[section],
                    spine[section + 1],
                    left[section + 1],
                    leftColor);
                AddDoubleSidedQuad(
                    draft,
                    spine[section],
                    right[section],
                    right[section + 1],
                    spine[section + 1],
                    rightColor);
            }

            // Four raking pylons carry the floating ribbon without turning the
            // footprint into a forest of car-port posts.
            AddOrientedBeam(
                draft,
                new Vector3(-12.7f, -2.8f, -8.9f),
                left[1],
                0.52f,
                dark);
            AddOrientedBeam(
                draft,
                new Vector3(12.7f, -2.8f, -8.1f),
                right[1],
                0.52f,
                dark);
            AddOrientedBeam(
                draft,
                new Vector3(-12.7f, -2.8f, 4.7f),
                left[7],
                0.52f,
                dark);
            AddOrientedBeam(
                draft,
                new Vector3(12.7f, -2.8f, 5.3f),
                right[7],
                0.52f,
                dark);

            // A blue seam follows the displaced ridge and ties the pavilion to
            // the retained painted P and illuminated parking identity.
            for (int section = 0; section < sectionCount - 1; section++)
            {
                AddOrientedBeam(
                    draft,
                    spine[section] + Vector3.up * 0.12f,
                    spine[section + 1] + Vector3.up * 0.12f,
                    0.34f,
                    blue);
            }
        }

        internal static float GetForecourtParkingMarkZOffset(
            UndergroundParkingStandaloneVariant variant)
        {
            return variant == UndergroundParkingStandaloneVariant.Grand
                ? 9f
                : 0f;
        }

        internal static float GetSurfacePavingLift(
            UndergroundParkingStandaloneVariant variant)
        {
            return SurfacePavingLift;
        }

        private static void AddForecourtParkingMark(
            MeshDraft draft,
            float variantZOffset,
            float surfaceLift)
        {
            Vector2 blue = Uv(0.65f, 0.65f);
            Vector2 white = Uv(0.9f, 0.9f);

            // The placed roadside prefab presents this forecourt toward local +Z. Build the
            // glyph for that viewing direction; the previous -Z-facing layout read backwards.
            float shift = 1.9f + variantZOffset;
            AddHorizontalQuad(draft, -2.3f, 2.3f, -5.15f + shift, -0.35f + shift, ForecourtPanelY + surfaceLift, blue);
            AddHorizontalQuad(draft, -2.42f, 2.42f, -5.27f + shift, -5.15f + shift, ForecourtGlyphY + surfaceLift, white);
            AddHorizontalQuad(draft, -2.42f, 2.42f, -0.35f + shift, -0.23f + shift, ForecourtGlyphY + surfaceLift, white);
            AddHorizontalQuad(draft, -2.42f, -2.3f, -5.15f + shift, -0.35f + shift, ForecourtGlyphY + surfaceLift, white);
            AddHorizontalQuad(draft, 2.3f, 2.42f, -5.15f + shift, -0.35f + shift, ForecourtGlyphY + surfaceLift, white);
            UndergroundParkingMarkGeometry.AddCenteredParkingSignP(
                draft.Vertices,
                draft.Uvs,
                null,
                draft.Triangles,
                0f,
                -2.75f + shift,
                2.5f,
                -1f,
                -1f,
                ForecourtGlyphY + surfaceLift,
                white);
        }

        private static Mesh CreateParkingMarkOverlayMesh(
            UndergroundParkingStandaloneVariant variant)
        {
            MeshDraft draft = new MeshDraft();
            Vector2 blue = new Vector2(0.25f, 0.5f);
            Vector2 white = new Vector2(0.75f, 0.5f);
            float shift = 1.9f + GetForecourtParkingMarkZOffset(variant);
            float surfaceLift = GetSurfacePavingLift(variant);
            const float horizontalLift = 0.012f;
            const float verticalLift = 0.012f;

            // A point-filtered, mip-free copy of only the parking artwork sits
            // just above the lit building mesh. This keeps the supplied blue and
            // white invariant while the concrete, tarmac and kiosk remain lit and
            // can acquire a wet specular response.
            AddHorizontalQuad(
                draft,
                -2.3f,
                2.3f,
                -5.15f + shift,
                -0.35f + shift,
                ForecourtPanelY + surfaceLift + horizontalLift,
                blue);
            AddHorizontalQuad(
                draft, -2.42f, 2.42f,
                -5.27f + shift, -5.15f + shift,
                ForecourtGlyphY + surfaceLift + horizontalLift, white);
            AddHorizontalQuad(
                draft, -2.42f, 2.42f,
                -0.35f + shift, -0.23f + shift,
                ForecourtGlyphY + surfaceLift + horizontalLift, white);
            AddHorizontalQuad(
                draft, -2.42f, -2.3f,
                -5.15f + shift, -0.35f + shift,
                ForecourtGlyphY + surfaceLift + horizontalLift, white);
            AddHorizontalQuad(
                draft, 2.3f, 2.42f,
                -5.15f + shift, -0.35f + shift,
                ForecourtGlyphY + surfaceLift + horizontalLift, white);
            UndergroundParkingMarkGeometry.AddCenteredParkingSignP(
                draft.Vertices,
                draft.Uvs,
                null,
                draft.Triangles,
                0f,
                -2.75f + shift,
                2.5f,
                -1f,
                -1f,
                ForecourtGlyphY + surfaceLift + horizontalLift,
                white);

            AddVerticalQuad(
                draft,
                -0.72f,
                0.72f,
                3.62f,
                5.06f,
                -3.44f + verticalLift,
                blue);
            UndergroundParkingMarkGeometry.AddCenteredParkingSignPVertical(
                draft.Vertices,
                draft.Uvs,
                null,
                draft.Triangles,
                0f,
                4.34f,
                0.92f,
                1f,
                1f,
                -3.42f + verticalLift,
                white);

            Mesh mesh = new Mesh();
            mesh.name = "Underground Parking Garage Exact P Overlay";
            mesh.vertices = draft.Vertices.ToArray();
            mesh.uv = draft.Uvs.ToArray();
            Color[] colors = new Color[draft.Uvs.Count];
            Color blueColor = UndergroundParkingMarkGeometry.ParkingBlueVertex;
            Color whiteColor = UndergroundParkingMarkGeometry.ParkingWhiteVertex;
            for (int i = 0; i < colors.Length; i++)
                colors[i] = draft.Uvs[i].x < 0.5f ? blueColor : whiteColor;
            mesh.colors = colors;
            mesh.triangles = draft.Triangles.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Vector2 Uv(float x, float y)
        {
            return new Vector2(x, y);
        }

        private static void AddHorizontalQuad(
            MeshDraft draft,
            float minX,
            float maxX,
            float minZ,
            float maxZ,
            float y,
            Vector2 uv)
        {
            AddQuad(
                draft,
                new Vector3(minX, y, minZ),
                new Vector3(maxX, y, minZ),
                new Vector3(maxX, y, maxZ),
                new Vector3(minX, y, maxZ),
                uv);
        }

        private static void AddHorizontalQuad(
            MeshDraft draft,
            float minX,
            float maxX,
            float minZ,
            float maxZ,
            float y,
            Rect uv)
        {
            AddQuad(
                draft,
                new Vector3(minX, y, minZ),
                new Vector3(maxX, y, minZ),
                new Vector3(maxX, y, maxZ),
                new Vector3(minX, y, maxZ),
                new Vector2(uv.xMin, uv.yMin),
                new Vector2(uv.xMax, uv.yMin),
                new Vector2(uv.xMax, uv.yMax),
                new Vector2(uv.xMin, uv.yMax));
        }

        private static void AddVerticalQuad(
            MeshDraft draft,
            float minX,
            float maxX,
            float minY,
            float maxY,
            float z,
            Vector2 uv)
        {
            AddQuad(
                draft,
                new Vector3(minX, minY, z),
                new Vector3(maxX, minY, z),
                new Vector3(maxX, maxY, z),
                new Vector3(minX, maxY, z),
                uv);
        }

        private static void AddBox(
            MeshDraft draft,
            float minX,
            float maxX,
            float minY,
            float maxY,
            float minZ,
            float maxZ,
            Vector2 uv)
        {
            Vector3 p000 = new Vector3(minX, minY, minZ);
            Vector3 p100 = new Vector3(maxX, minY, minZ);
            Vector3 p110 = new Vector3(maxX, maxY, minZ);
            Vector3 p010 = new Vector3(minX, maxY, minZ);
            Vector3 p001 = new Vector3(minX, minY, maxZ);
            Vector3 p101 = new Vector3(maxX, minY, maxZ);
            Vector3 p111 = new Vector3(maxX, maxY, maxZ);
            Vector3 p011 = new Vector3(minX, maxY, maxZ);

            AddQuad(draft, p000, p100, p110, p010, uv);
            AddQuad(draft, p101, p001, p011, p111, uv);
            AddQuad(draft, p001, p000, p010, p011, uv);
            AddQuad(draft, p100, p101, p111, p110, uv);
            AddQuad(draft, p010, p110, p111, p011, uv);
            AddQuad(draft, p001, p101, p100, p000, uv);
        }

        private static void AddDoubleSidedQuad(
            MeshDraft draft,
            Vector3 a,
            Vector3 b,
            Vector3 c,
            Vector3 d,
            Vector2 uv)
        {
            AddQuad(draft, a, b, c, d, uv);
            AddQuad(draft, d, c, b, a, uv);
        }

        private static void AddOrientedBeam(
            MeshDraft draft,
            Vector3 start,
            Vector3 end,
            float thickness,
            Vector2 uv)
        {
            Vector3 axis = end - start;
            if (axis.sqrMagnitude < 0.0001f || thickness <= 0f)
                return;

            axis.Normalize();
            Vector3 reference = Mathf.Abs(Vector3.Dot(axis, Vector3.up)) > 0.92f
                ? Vector3.forward
                : Vector3.up;
            Vector3 side = Vector3.Cross(axis, reference).normalized * (thickness * 0.5f);
            Vector3 normal = Vector3.Cross(side, axis).normalized * (thickness * 0.5f);

            Vector3 start00 = start - side - normal;
            Vector3 start10 = start + side - normal;
            Vector3 start11 = start + side + normal;
            Vector3 start01 = start - side + normal;
            Vector3 end00 = end - side - normal;
            Vector3 end10 = end + side - normal;
            Vector3 end11 = end + side + normal;
            Vector3 end01 = end - side + normal;

            AddQuad(draft, start00, start10, start11, start01, uv);
            AddQuad(draft, end10, end00, end01, end11, uv);
            AddQuad(draft, end00, start00, start01, end01, uv);
            AddQuad(draft, start10, end10, end11, start11, uv);
            AddQuad(draft, start01, start11, end11, end01, uv);
            AddQuad(draft, end00, end10, start10, start00, uv);
        }

        private static void AddQuad(
            MeshDraft draft,
            Vector3 a,
            Vector3 b,
            Vector3 c,
            Vector3 d,
            Vector2 uv)
        {
            int start = draft.Vertices.Count;
            draft.Vertices.Add(a);
            draft.Vertices.Add(b);
            draft.Vertices.Add(c);
            draft.Vertices.Add(d);
            draft.Uvs.Add(uv);
            draft.Uvs.Add(uv);
            draft.Uvs.Add(uv);
            draft.Uvs.Add(uv);
            draft.Triangles.Add(start);
            draft.Triangles.Add(start + 2);
            draft.Triangles.Add(start + 1);
            draft.Triangles.Add(start);
            draft.Triangles.Add(start + 3);
            draft.Triangles.Add(start + 2);
        }

        private static void AddQuad(
            MeshDraft draft,
            Vector3 a,
            Vector3 b,
            Vector3 c,
            Vector3 d,
            Vector2 uvA,
            Vector2 uvB,
            Vector2 uvC,
            Vector2 uvD)
        {
            int start = draft.Vertices.Count;
            draft.Vertices.Add(a);
            draft.Vertices.Add(b);
            draft.Vertices.Add(c);
            draft.Vertices.Add(d);
            draft.Uvs.Add(uvA);
            draft.Uvs.Add(uvB);
            draft.Uvs.Add(uvC);
            draft.Uvs.Add(uvD);
            draft.Triangles.Add(start);
            draft.Triangles.Add(start + 2);
            draft.Triangles.Add(start + 1);
            draft.Triangles.Add(start);
            draft.Triangles.Add(start + 3);
            draft.Triangles.Add(start + 2);
        }

        private sealed class MeshDraft
        {
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public readonly List<Vector2> Uvs = new List<Vector2>();
            public readonly List<int> Triangles = new List<int>();
        }
    }
}
