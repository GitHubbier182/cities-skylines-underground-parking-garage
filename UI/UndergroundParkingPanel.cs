using ColossalFramework.UI;
using UnityEngine;

namespace UndergroundParkingGarage
{
    public class UndergroundParkingPanel : UIPanel
    {
        private const float PanelWidth = 388f;
        private const float PanelHeight = 96f;
        internal const string PlacementIconSprite = "UndergroundParkingPlacementIcon";
        public static UndergroundParkingPanel Instance;

        private static readonly UITextureAtlas[] VariantIconAtlases =
            new UITextureAtlas[UndergroundParkingStandaloneCatalog.VariantCount];
        private UIButton _compactTile;
        private UIButton _grandTile;
        private UIButton _squareTile;
        private UIButton _buildingAttachedTile;

        public override void Start()
        {
            base.Start();

            Instance = this;
            name = "UndergroundParkingGaragePanel";
            width = PanelWidth;
            height = PanelHeight;
            backgroundSprite = string.Empty;
            color = new Color32(255, 255, 255, 255);
            canFocus = true;
            isInteractive = true;
            isVisible = true;

            BuildBody();
            Refresh();
        }

        public static void DestroyInstance()
        {
            if (Instance == null)
                return;

            Object.Destroy(Instance.gameObject);
            Instance = null;
        }

        public static void RefreshInstance()
        {
            if (Instance != null)
                Instance.Refresh();
        }

        public static void UpdateButtonState()
        {
            if (Instance != null)
                Instance.Refresh();
        }

        public override void OnDestroy()
        {
            if (Instance == this)
                Instance = null;

            base.OnDestroy();
        }

        private void BuildBody()
        {
            _compactTile = AddPlacementTile(
                UndergroundParkingStandaloneVariant.Compact,
                new Vector3(8f, 8f),
                "2×3",
                "Compact 2x3 entrance • 50m x 25m underground garage");
            _grandTile = AddPlacementTile(
                UndergroundParkingStandaloneVariant.Grand,
                new Vector3(192f, 8f),
                "4×4",
                "Grand 4x4 pavilion • two floors • 8x10-cell underground garage");
            _squareTile = AddPlacementTile(
                UndergroundParkingStandaloneVariant.Square,
                new Vector3(100f, 8f),
                "3×3",
                "Civic 3x3 pavilion • 8x8-cell underground garage");
            _buildingAttachedTile = AddBuildingAttachedTile();
        }

        private void Refresh()
        {
            long startedAt = UndergroundParkingTabPerformanceDiagnostics.BeginCallbackSample();
            try
            {
                if (_compactTile == null)
                    return;

                RefreshVariantTile(_compactTile, UndergroundParkingStandaloneVariant.Compact);
                RefreshVariantTile(_grandTile, UndergroundParkingStandaloneVariant.Grand);
                RefreshVariantTile(_squareTile, UndergroundParkingStandaloneVariant.Square);
                if (_buildingAttachedTile != null)
                {
                    _buildingAttachedTile.normalBgSprite = UndergroundParkingPlacementTool.Active ? "ButtonMenuPressed" : "ButtonMenu";
                    _buildingAttachedTile.tooltip = "Add parking beneath an existing building";
                }
            }
            finally
            {
                UndergroundParkingTabPerformanceDiagnostics.EndPanelRepaint(startedAt);
            }
        }

        private static void RefreshVariantTile(
            UIButton tile,
            UndergroundParkingStandaloneVariant variant)
        {
            if (tile != null)
                tile.normalBgSprite =
                    UndergroundParkingBuildingPlacement.IsActiveVariant(variant)
                        ? "ButtonMenuPressed"
                        : "ButtonMenu";
        }

        private UIButton AddPlacementTile(
            UndergroundParkingStandaloneVariant variant,
            Vector3 position,
            string badgeText,
            string tooltip)
        {
            UIButton button = AddUIComponent<UIButton>();
            button.name = "UndergroundParkingGaragePlacementTile" + variant;
            button.width = 88f;
            button.height = 82f;
            button.relativePosition = position;
            button.text = string.Empty;
            button.normalBgSprite = "ButtonMenu";
            button.hoveredBgSprite = "ButtonMenuHovered";
            button.pressedBgSprite = "ButtonMenuPressed";
            button.disabledBgSprite = "ButtonMenuDisabled";
            EnsureIconSprite(button, 56f, variant);
            AddVariantBadge(button, badgeText);
            if (!UndergroundParkingInfoTooltip.Bind(button, variant))
                button.tooltip = tooltip;
            button.eventClick += delegate
            {
                if (UndergroundParkingBuildingPlacement.IsActiveVariant(variant))
                    UndergroundParkingBuildingPlacement.Deactivate();
                else
                    UndergroundParkingBuildingPlacement.Activate(variant);
                Refresh();
            };
            return button;
        }

        private UIButton AddBuildingAttachedTile()
        {
            UIButton button = AddUIComponent<UIButton>();
            button.name = "UndergroundParkingGarageBuildingAttachedTile";
            button.width = 88f;
            button.height = 82f;
            button.relativePosition = new Vector3(284f, 8f);
            button.text = string.Empty;
            button.normalBgSprite = "ButtonMenu";
            button.hoveredBgSprite = "ButtonMenuHovered";
            button.pressedBgSprite = "ButtonMenuPressed";
            button.disabledBgSprite = "ButtonMenuDisabled";
            EnsureIconSprite(button, 56f);

            UILabel badge = button.AddUIComponent<UILabel>();
            badge.name = "BuildingAttachedBadge";
            badge.text = "B";
            badge.textScale = 0.9f;
            badge.textAlignment = UIHorizontalAlignment.Center;
            badge.verticalAlignment = UIVerticalAlignment.Middle;
            badge.width = 22f;
            badge.height = 22f;
            badge.relativePosition = new Vector3(60f, 56f);
            badge.color = new Color32(255, 255, 255, 255);

            button.tooltip = "Add parking beneath an existing building";
            button.eventClick += OnBuildingAttachedClicked;
            return button;
        }

        private static void AddVariantBadge(UIButton button, string text)
        {
            UILabel badge = button.AddUIComponent<UILabel>();
            badge.name = "StandaloneVariantBadge";
            badge.text = text;
            badge.textScale = 0.58f;
            badge.textAlignment = UIHorizontalAlignment.Center;
            badge.verticalAlignment = UIVerticalAlignment.Middle;
            badge.width = 30f;
            badge.height = 18f;
            badge.relativePosition = new Vector3(54f, 59f);
            badge.color = new Color32(255, 255, 255, 255);
            badge.backgroundSprite = "GenericPanel";
        }

        private void OnBuildingAttachedClicked(UIComponent component, UIMouseEventParameter p)
        {
            UndergroundParkingBuildingPlacement.Deactivate();
            UndergroundParkingPlacementTool.ReassertPlacementMode();

            Refresh();
        }

        internal static void EnsureIconSprite(UIComponent parent, float size)
        {
            EnsureIconSprite(
                parent,
                size,
                UndergroundParkingStandaloneVariant.Compact);
        }

        internal static void EnsureIconSprite(
            UIComponent parent,
            float size,
            UndergroundParkingStandaloneVariant variant)
        {
            if (parent == null)
                return;

            UITextureAtlas atlas = GetOrCreateIconAtlas(variant);
            if (atlas == null)
                return;

            UISprite icon = null;
            UISprite[] sprites = parent.GetComponentsInChildren<UISprite>(true);
            for (int i = 0; i < sprites.Length; i++)
            {
                if (sprites[i] != null && sprites[i].name == "UndergroundParkingGarageIcon")
                {
                    icon = sprites[i];
                    break;
                }
            }

            if (icon == null)
            {
                icon = parent.AddUIComponent<UISprite>();
                icon.name = "UndergroundParkingGarageIcon";
            }

            icon.atlas = atlas;
            icon.spriteName = PlacementIconSprite;
            icon.width = size;
            icon.height = size;
            icon.relativePosition = new Vector3(
                Mathf.Max(0f, (parent.width - size) * 0.5f),
                Mathf.Max(0f, (parent.height - size) * 0.5f));
            icon.isInteractive = false;
            icon.isVisible = true;
        }

        internal static UITextureAtlas GetOrCreateIconAtlas()
        {
            return GetOrCreateIconAtlas(
                UndergroundParkingStandaloneVariant.Compact);
        }

        internal static UITextureAtlas GetOrCreateIconAtlas(
            UndergroundParkingStandaloneVariant variant)
        {
            int variantIndex = (int)variant;
            if (variantIndex < 0 || variantIndex >= VariantIconAtlases.Length)
                variantIndex = 0;
            if (VariantIconAtlases[variantIndex] != null)
                return VariantIconAtlases[variantIndex];

            UIView view = UIView.GetAView();
            if (view == null || view.defaultAtlas == null || view.defaultAtlas.material == null)
                return null;

            Texture2D texture = CreatePlacementIconTexture(
                (UndergroundParkingStandaloneVariant)variantIndex);
            Material material = new Material(view.defaultAtlas.material);
            material.mainTexture = texture;
            UITextureAtlas atlas = ScriptableObject.CreateInstance<UITextureAtlas>();
            atlas.name = "UndergroundParkingGarageIconAtlas";
            atlas.material = material;
            atlas.AddSprite(new UITextureAtlas.SpriteInfo
            {
                name = PlacementIconSprite,
                texture = texture,
                region = new Rect(0f, 0f, 1f, 1f),
                border = new RectOffset()
            });

            VariantIconAtlases[variantIndex] = atlas;
            return atlas;
        }

        private static Texture2D CreatePlacementIconTexture(
            UndergroundParkingStandaloneVariant variant)
        {
            const int size = 512;
            Texture2D texture = new Texture2D(size, size, TextureFormat.ARGB32, true);
            texture.hideFlags = HideFlags.HideAndDontSave;
            Color32 clear = new Color32(0, 0, 0, 0);
            Color32[] pixels = new Color32[size * size];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = clear;

            Color32 navy = new Color32(18, 39, 58, 248);
            Color32 navyEdge = new Color32(77, 151, 196, 255);
            Color32 concrete = new Color32(185, 203, 214, 255);
            Color32 concreteLight = new Color32(225, 237, 242, 255);
            Color32 asphalt = new Color32(45, 53, 61, 255);
            Color32 asphaltLight = new Color32(75, 87, 96, 255);
            Color32 portal = new Color32(8, 17, 27, 255);
            Color32 blue = new Color32(28, 102, 220, 255);
            Color32 blueLight = new Color32(49, 157, 248, 255);
            Color32 white = new Color32(250, 253, 255, 255);
            Color32 yellow = new Color32(255, 202, 55, 255);
            Color32 shadow = new Color32(0, 0, 0, 115);

            // A large, high-contrast tile silhouette survives the game's 56px display size.
            FillRoundedRect(pixels, size, 18, 18, 476, 476, 58, navyEdge);
            FillRoundedRect(pixels, size, 28, 28, 456, 456, 50, navy);

            // Road frontage and kerb establish that this is a placeable roadside entrance.
            FillPolygon(pixels, size, new[]
            {
                new Vector2(42, 348), new Vector2(470, 348),
                new Vector2(470, 466), new Vector2(42, 466)
            }, asphalt);
            FillRect(pixels, size, size, 42, 342, 428, 14, concreteLight);
            FillRect(pixels, size, size, 42, 449, 428, 8, asphaltLight);
            FillRect(pixels, size, size, 66, 402, 92, 10, white);
            FillRect(pixels, size, size, 354, 402, 92, 10, white);
            FillRect(pixels, size, size, 226, 400, 60, 13, yellow);

            // Deep garage opening, substantial canopy and side piers.
            FillRoundedRect(pixels, size, 91, 84, 330, 258, 25, shadow);
            FillRoundedRect(pixels, size, 104, 73, 304, 252, 22, concrete);
            FillRect(pixels, size, size, 125, 124, 262, 201, portal);
            FillRect(pixels, size, size, 104, 73, 304, 57, concreteLight);
            FillRect(pixels, size, size, 104, 118, 304, 25, blueLight);
            FillRect(pixels, size, size, 104, 134, 34, 191, concreteLight);
            FillRect(pixels, size, size, 374, 134, 34, 191, concreteLight);

            // Perspective ramp descends from the road into the dark portal.
            FillPolygon(pixels, size, new[]
            {
                new Vector2(137, 325), new Vector2(375, 325),
                new Vector2(426, 448), new Vector2(86, 448)
            }, asphaltLight);
            DrawLine(pixels, size, new Vector2(137, 325), new Vector2(86, 448), 10f, concreteLight);
            DrawLine(pixels, size, new Vector2(375, 325), new Vector2(426, 448), 10f, concreteLight);
            DrawLine(pixels, size, new Vector2(256, 346), new Vector2(256, 382), 9f, yellow);
            DrawLine(pixels, size, new Vector2(256, 405), new Vector2(256, 438), 12f, yellow);

            // Prominent parking sign: bordered blue panel and an unmistakable capital P.
            FillRoundedRect(pixels, size, 174, 153, 164, 142, 18, white);
            FillRoundedRect(pixels, size, 184, 163, 144, 122, 12, blue);
            FillRect(pixels, size, size, 216, 185, 24, 78, white);
            FillRoundedRect(pixels, size, 232, 185, 66, 52, 14, white);
            FillRoundedRect(pixels, size, 240, 195, 42, 30, 8, blue);

            // A clear downward chevron reinforces underground access at tiny scale.
            DrawLine(pixels, size, new Vector2(220, 300), new Vector2(256, 326), 13f, white);
            DrawLine(pixels, size, new Vector2(256, 326), new Vector2(292, 300), 13f, white);

            // Each standalone option has its own generated footprint symbol.
            // The adjacent UI badge supplies exact dimensions at 56px.
            if (variant == UndergroundParkingStandaloneVariant.Grand)
            {
                FillRoundedRect(pixels, size, 42, 42, 128, 82, 12, blueLight);
                FillRoundedRect(pixels, size, 54, 54, 104, 58, 7, portal);
                DrawLine(pixels, size, new Vector2(68, 68), new Vector2(144, 68), 6f, white);
                DrawLine(pixels, size, new Vector2(68, 83), new Vector2(144, 83), 6f, white);
                DrawLine(pixels, size, new Vector2(68, 98), new Vector2(144, 98), 6f, white);
            }
            else if (variant == UndergroundParkingStandaloneVariant.Square)
            {
                FillRoundedRect(pixels, size, 48, 42, 92, 92, 12, blueLight);
                FillRoundedRect(pixels, size, 60, 54, 68, 68, 7, portal);
                DrawLine(pixels, size, new Vector2(94, 57), new Vector2(94, 119), 5f, white);
                DrawLine(pixels, size, new Vector2(63, 88), new Vector2(125, 88), 5f, white);
            }
            else
            {
                FillRoundedRect(pixels, size, 50, 48, 72, 92, 10, blueLight);
                FillRoundedRect(pixels, size, 61, 59, 50, 70, 6, portal);
                DrawLine(pixels, size, new Vector2(86, 62), new Vector2(86, 126), 5f, white);
            }

            FlipVertically(pixels, size);
            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Trilinear;
            texture.anisoLevel = 2;
            return texture;
        }

        private static void FlipVertically(Color32[] pixels, int size)
        {
            for (int y = 0; y < size / 2; y++)
            {
                int oppositeY = size - 1 - y;
                for (int x = 0; x < size; x++)
                {
                    int first = y * size + x;
                    int second = oppositeY * size + x;
                    Color32 value = pixels[first];
                    pixels[first] = pixels[second];
                    pixels[second] = value;
                }
            }
        }

        private static void FillRoundedRect(
            Color32[] pixels,
            int size,
            int x,
            int y,
            int width,
            int height,
            int radius,
            Color32 color)
        {
            float left = x + radius;
            float right = x + width - radius - 1;
            float bottom = y + radius;
            float top = y + height - radius - 1;
            for (int yy = Mathf.Max(0, y); yy < Mathf.Min(size, y + height); yy++)
            {
                for (int xx = Mathf.Max(0, x); xx < Mathf.Min(size, x + width); xx++)
                {
                    float nearestX = Mathf.Clamp(xx, left, right);
                    float nearestY = Mathf.Clamp(yy, bottom, top);
                    float dx = xx - nearestX;
                    float dy = yy - nearestY;
                    if (dx * dx + dy * dy <= radius * radius)
                        pixels[yy * size + xx] = color;
                }
            }
        }

        private static void FillPolygon(Color32[] pixels, int size, Vector2[] points, Color32 color)
        {
            if (points == null || points.Length < 3)
                return;

            int minX = size - 1;
            int maxX = 0;
            int minY = size - 1;
            int maxY = 0;
            for (int i = 0; i < points.Length; i++)
            {
                minX = Mathf.Min(minX, Mathf.FloorToInt(points[i].x));
                maxX = Mathf.Max(maxX, Mathf.CeilToInt(points[i].x));
                minY = Mathf.Min(minY, Mathf.FloorToInt(points[i].y));
                maxY = Mathf.Max(maxY, Mathf.CeilToInt(points[i].y));
            }

            for (int y = Mathf.Max(0, minY); y <= Mathf.Min(size - 1, maxY); y++)
            {
                for (int x = Mathf.Max(0, minX); x <= Mathf.Min(size - 1, maxX); x++)
                {
                    bool inside = false;
                    for (int i = 0, j = points.Length - 1; i < points.Length; j = i++)
                    {
                        Vector2 a = points[i];
                        Vector2 b = points[j];
                        if ((a.y > y) != (b.y > y)
                            && x < (b.x - a.x) * (y - a.y) / (b.y - a.y) + a.x)
                        {
                            inside = !inside;
                        }
                    }
                    if (inside)
                        pixels[y * size + x] = color;
                }
            }
        }

        private static void DrawLine(
            Color32[] pixels,
            int size,
            Vector2 start,
            Vector2 end,
            float thickness,
            Color32 color)
        {
            Vector2 delta = end - start;
            float lengthSqr = Mathf.Max(0.001f, delta.sqrMagnitude);
            float radius = thickness * 0.5f;
            int minX = Mathf.FloorToInt(Mathf.Min(start.x, end.x) - radius);
            int maxX = Mathf.CeilToInt(Mathf.Max(start.x, end.x) + radius);
            int minY = Mathf.FloorToInt(Mathf.Min(start.y, end.y) - radius);
            int maxY = Mathf.CeilToInt(Mathf.Max(start.y, end.y) + radius);
            for (int y = Mathf.Max(0, minY); y <= Mathf.Min(size - 1, maxY); y++)
            {
                for (int x = Mathf.Max(0, minX); x <= Mathf.Min(size - 1, maxX); x++)
                {
                    Vector2 point = new Vector2(x, y);
                    float t = Mathf.Clamp01(Vector2.Dot(point - start, delta) / lengthSqr);
                    if ((point - (start + delta * t)).sqrMagnitude <= radius * radius)
                        pixels[y * size + x] = color;
                }
            }
        }

        private static void FillRect(Color32[] pixels, int width, int height, int x, int y, int w, int h, Color32 color)
        {
            for (int yy = Mathf.Max(0, y); yy < Mathf.Min(height, y + h); yy++)
            {
                int row = yy * width;
                for (int xx = Mathf.Max(0, x); xx < Mathf.Min(width, x + w); xx++)
                    pixels[row + xx] = color;
            }
        }
    }
}
