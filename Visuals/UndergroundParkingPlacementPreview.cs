using System.Collections.Generic;
using UnityEngine;

namespace UndergroundParkingGarage
{
    internal static class UndergroundParkingPlacementPreview
    {
        private const float SurfaceLift = 0.14f;
        private const float KioskWidth = 5.4f;
        private const float KioskLength = 4.4f;
        private const float KioskHeight = 3.8f;

        private static GameObject _root;
        private static MeshRenderer _footprintRenderer;
        private static MeshRenderer _frameRenderer;
        private static MeshRenderer _arrowRenderer;
        private static MeshRenderer _kioskRenderer;
        private static Material _validFootprintMaterial;
        private static Material _invalidFootprintMaterial;
        private static Material _validFrameMaterial;
        private static Material _invalidFrameMaterial;
        private static Material _validGridMaterial;
        private static Material _invalidGridMaterial;
        private static Material _validKioskMaterial;
        private static Material _invalidKioskMaterial;
        private static Material _signMaterial;
        private static Mesh _footprintMesh;
        private static Mesh _frameMesh;
        private static Mesh _gridMesh;
        private static Mesh _arrowMesh;
        private static Mesh _kioskMesh;
        private static Mesh _signMesh;
        private static UndergroundParkingFacility _currentFacility;
        private static bool _currentValid;
        private static bool _hasPreview;

        public static void UpdatePreview(UndergroundParkingFacility facility, bool valid, string message)
        {
            if (facility.SurfaceSegmentId == 0)
            {
                Clear();
                return;
            }

            _currentFacility = facility;
            _currentValid = valid;
            _hasPreview = true;

            if (_root == null)
                return;

            _root.SetActive(false);
            _root.transform.position = facility.EntrancePosition + Vector3.up * SurfaceLift;
            _root.transform.rotation = Quaternion.LookRotation(facility.Side, Vector3.up);

            if (_footprintRenderer != null)
                _footprintRenderer.material = valid ? GetValidFootprintMaterial() : GetInvalidFootprintMaterial();
            if (_frameRenderer != null)
                _frameRenderer.material = valid ? GetValidFrameMaterial() : GetInvalidFrameMaterial();
            if (_arrowRenderer != null)
                _arrowRenderer.material = valid ? GetValidFrameMaterial() : GetInvalidFrameMaterial();
            if (_kioskRenderer != null)
                _kioskRenderer.material = valid ? GetValidKioskMaterial() : GetInvalidKioskMaterial();
        }

        public static void Clear()
        {
            _hasPreview = false;
            _currentValid = false;
            _currentFacility = UndergroundParkingFacility.None;

            if (_root != null)
                _root.SetActive(false);
        }

        public static void Shutdown()
        {
            if (_root != null)
            {
                Object.Destroy(_root);
                _root = null;
            }
        }

        public static void RenderOverlay(RenderManager.CameraInfo cameraInfo)
        {
            if (!_hasPreview || _currentFacility.SurfaceSegmentId == 0 || cameraInfo == null)
                return;

            Vector3 position = _currentFacility.EntrancePosition + Vector3.up * SurfaceLift;
            if (!cameraInfo.CheckRenderDistance(position, 4096f))
                return;

            // The committed surface marker is a real loaded parking-sign prop.
            // Do not fake it with the old generated P quad in preview.
        }

        private static void EnsureRoot()
        {
            if (_root != null)
                return;

            _root = new GameObject("UndergroundParkingGaragePlacementPreview");
            Object.DontDestroyOnLoad(_root);

            _footprintRenderer = AddMeshChild(
                _root,
                "2x3-footprint",
                GetFootprintMesh(),
                GetValidFootprintMaterial(),
                Vector3.zero);

            _frameRenderer = AddMeshChild(
                _root,
                "2x3-footprint-frame",
                GetFrameMesh(),
                GetValidFrameMaterial(),
                new Vector3(0f, 0.05f, 0f));

            _arrowRenderer = AddMeshChild(
                _root,
                "road-frontage-arrow",
                GetArrowMesh(),
                GetValidFrameMaterial(),
                new Vector3(0f, 0.08f, -UndergroundParkingGeometry.EntranceLotLength * 0.68f));

            _kioskRenderer = AddMeshChild(
                _root,
                "entrance-kiosk",
                GetKioskMesh(),
                GetValidKioskMaterial(),
                new Vector3(0f, KioskHeight * 0.5f, -UndergroundParkingGeometry.EntranceLotLength * 0.28f));

            AddMeshChild(
                _root,
                "parking-sign",
                GetSignMesh(),
                GetSignMaterial(),
                new Vector3(0f, KioskHeight + 0.25f, -UndergroundParkingGeometry.EntranceLotLength * 0.48f));
        }

        private static MeshRenderer AddMeshChild(GameObject parent, string name, Mesh mesh, Material material, Vector3 localPosition)
        {
            GameObject child = new GameObject(name);
            child.transform.parent = parent.transform;
            child.transform.localPosition = localPosition;
            child.transform.localRotation = Quaternion.identity;
            child.transform.localScale = Vector3.one;

            MeshFilter filter = child.AddComponent<MeshFilter>();
            filter.mesh = mesh;
            MeshRenderer renderer = child.AddComponent<MeshRenderer>();
            renderer.material = material;
            return renderer;
        }

        private static Mesh GetFootprintMesh()
        {
            if (_footprintMesh == null)
            {
                _footprintMesh = CreateFlatQuad(
                    "Underground Parking Garage 2x3 Footprint Preview",
                    UndergroundParkingGeometry.EntranceLotWidth,
                    UndergroundParkingGeometry.EntranceLotLength);
            }

            return _footprintMesh;
        }

        private static Mesh GetFrameMesh()
        {
            if (_frameMesh == null)
            {
                _frameMesh = CreateFrameMesh(
                    "Underground Parking Garage 2x3 Footprint Frame",
                    UndergroundParkingGeometry.EntranceLotWidth,
                    UndergroundParkingGeometry.EntranceLotLength,
                    0.55f);
            }

            return _frameMesh;
        }

        private static Mesh GetGridMesh()
        {
            if (_gridMesh == null)
            {
                _gridMesh = CreateCellMatrixMesh(
                    "Underground Parking Garage 2x3 Footprint Matrix",
                    UndergroundParkingGeometry.EntranceLotWidth,
                    UndergroundParkingGeometry.EntranceLotLength,
                    UndergroundParkingGeometry.BuildingCellSize,
                    0.28f);
            }

            return _gridMesh;
        }

        private static Mesh GetArrowMesh()
        {
            if (_arrowMesh == null)
                _arrowMesh = CreateArrowMesh("Underground Parking Garage Frontage Arrow", 7.2f, 9.4f);
            return _arrowMesh;
        }

        private static Mesh GetKioskMesh()
        {
            if (_kioskMesh == null)
                _kioskMesh = CreateBoxMesh("Underground Parking Garage Preview Kiosk", KioskWidth, KioskHeight, KioskLength);
            return _kioskMesh;
        }

        private static Mesh GetSignMesh()
        {
            if (_signMesh == null)
                _signMesh = CreateVerticalQuad("Underground Parking Garage Preview Sign", 3.1f, 3.1f);
            return _signMesh;
        }

        private static Mesh CreateFlatQuad(string name, float width, float length)
        {
            Mesh mesh = new Mesh();
            mesh.name = name;
            float halfW = width * 0.5f;
            float halfL = length * 0.5f;
            mesh.vertices = new[]
            {
                new Vector3(-halfW, 0f, -halfL),
                new Vector3(halfW, 0f, -halfL),
                new Vector3(halfW, 0f, halfL),
                new Vector3(-halfW, 0f, halfL)
            };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f)
            };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateFrameMesh(string name, float width, float length, float thickness)
        {
            Mesh mesh = new Mesh();
            mesh.name = name;
            float halfW = width * 0.5f;
            float halfL = length * 0.5f;
            float insetW = Mathf.Max(0f, halfW - thickness);
            float insetL = Mathf.Max(0f, halfL - thickness);
            mesh.vertices = new[]
            {
                new Vector3(-halfW, 0f, -halfL),
                new Vector3(halfW, 0f, -halfL),
                new Vector3(halfW, 0f, halfL),
                new Vector3(-halfW, 0f, halfL),
                new Vector3(-insetW, 0f, -insetL),
                new Vector3(insetW, 0f, -insetL),
                new Vector3(insetW, 0f, insetL),
                new Vector3(-insetW, 0f, insetL)
            };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f)
            };
            mesh.triangles = new[]
            {
                0, 5, 1, 0, 4, 5,
                1, 6, 2, 1, 5, 6,
                2, 7, 3, 2, 6, 7,
                3, 4, 0, 3, 7, 4
            };
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateCellMatrixMesh(string name, float width, float length, float cellSize, float thickness)
        {
            Mesh mesh = new Mesh();
            mesh.name = name;

            float halfW = width * 0.5f;
            float halfL = length * 0.5f;
            float halfThickness = thickness * 0.5f;

            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();

            AddFlatRect(vertices, triangles, -halfThickness, -halfL, halfThickness, halfL);
            for (float z = -halfL + cellSize; z < halfL - 0.01f; z += cellSize)
                AddFlatRect(vertices, triangles, -halfW, z - halfThickness, halfW, z + halfThickness);

            mesh.vertices = vertices.ToArray();
            mesh.triangles = triangles.ToArray();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void AddFlatRect(List<Vector3> vertices, List<int> triangles, float minX, float minZ, float maxX, float maxZ)
        {
            int index = vertices.Count;
            vertices.Add(new Vector3(minX, 0f, minZ));
            vertices.Add(new Vector3(maxX, 0f, minZ));
            vertices.Add(new Vector3(maxX, 0f, maxZ));
            vertices.Add(new Vector3(minX, 0f, maxZ));

            triangles.Add(index);
            triangles.Add(index + 2);
            triangles.Add(index + 1);
            triangles.Add(index);
            triangles.Add(index + 3);
            triangles.Add(index + 2);
        }

        private static Mesh CreateArrowMesh(string name, float width, float length)
        {
            Mesh mesh = new Mesh();
            mesh.name = name;
            float halfW = width * 0.5f;
            float shaftW = width * 0.32f;
            float shaftHalf = shaftW * 0.5f;
            float headStart = length * 0.42f;
            mesh.vertices = new[]
            {
                new Vector3(-shaftHalf, 0f, 0f),
                new Vector3(shaftHalf, 0f, 0f),
                new Vector3(shaftHalf, 0f, headStart),
                new Vector3(halfW, 0f, headStart),
                new Vector3(0f, 0f, length),
                new Vector3(-halfW, 0f, headStart),
                new Vector3(-shaftHalf, 0f, headStart)
            };
            mesh.uv = new[]
            {
                new Vector2(0.35f, 0f),
                new Vector2(0.65f, 0f),
                new Vector2(0.65f, 0.42f),
                new Vector2(1f, 0.42f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, 0.42f),
                new Vector2(0.35f, 0.42f)
            };
            mesh.triangles = new[]
            {
                0, 1, 2,
                0, 2, 6,
                6, 2, 3,
                6, 3, 5,
                5, 3, 4
            };
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateVerticalQuad(string name, float width, float height)
        {
            Mesh mesh = new Mesh();
            mesh.name = name;
            float halfW = width * 0.5f;
            mesh.vertices = new[]
            {
                new Vector3(-halfW, 0f, 0f),
                new Vector3(halfW, 0f, 0f),
                new Vector3(halfW, height, 0f),
                new Vector3(-halfW, height, 0f)
            };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f)
            };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateBoxMesh(string name, float width, float height, float length)
        {
            Mesh mesh = new Mesh();
            mesh.name = name;
            float halfW = width * 0.5f;
            float halfL = length * 0.5f;
            float halfH = height * 0.5f;
            mesh.vertices = new[]
            {
                new Vector3(-halfW, -halfH, -halfL),
                new Vector3(halfW, -halfH, -halfL),
                new Vector3(halfW, -halfH, halfL),
                new Vector3(-halfW, -halfH, halfL),
                new Vector3(-halfW, halfH, -halfL),
                new Vector3(halfW, halfH, -halfL),
                new Vector3(halfW, halfH, halfL),
                new Vector3(-halfW, halfH, halfL)
            };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f)
            };
            mesh.triangles = new[]
            {
                0, 2, 1, 0, 3, 2,
                4, 5, 6, 4, 6, 7,
                0, 1, 5, 0, 5, 4,
                1, 2, 6, 1, 6, 5,
                2, 3, 7, 2, 7, 6,
                3, 0, 4, 3, 4, 7
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Material GetValidFootprintMaterial()
        {
            if (_validFootprintMaterial == null)
                _validFootprintMaterial = CreateTransparentMaterial(new Color(0.36f, 0.92f, 0.26f, 0.38f), null, false);
            return _validFootprintMaterial;
        }

        private static Material GetInvalidFootprintMaterial()
        {
            if (_invalidFootprintMaterial == null)
                _invalidFootprintMaterial = CreateTransparentMaterial(new Color(1f, 0.08f, 0.08f, 0.42f), null, false);
            return _invalidFootprintMaterial;
        }

        private static Material GetValidFrameMaterial()
        {
            if (_validFrameMaterial == null)
                _validFrameMaterial = CreateTransparentMaterial(new Color(0.22f, 0.86f, 0.04f, 0.92f), null, false);
            return _validFrameMaterial;
        }

        private static Material GetInvalidFrameMaterial()
        {
            if (_invalidFrameMaterial == null)
                _invalidFrameMaterial = CreateTransparentMaterial(new Color(1f, 0.05f, 0.05f, 0.92f), null, false);
            return _invalidFrameMaterial;
        }

        private static Material GetValidGridMaterial()
        {
            if (_validGridMaterial == null)
                _validGridMaterial = CreateTransparentMaterial(new Color(0.03f, 0.22f, 0.02f, 0.84f), null, false);
            return _validGridMaterial;
        }

        private static Material GetInvalidGridMaterial()
        {
            if (_invalidGridMaterial == null)
                _invalidGridMaterial = CreateTransparentMaterial(new Color(0.32f, 0.02f, 0.02f, 0.84f), null, false);
            return _invalidGridMaterial;
        }

        private static Material GetValidKioskMaterial()
        {
            if (_validKioskMaterial == null)
                _validKioskMaterial = CreateTransparentMaterial(new Color(0.12f, 0.18f, 0.23f, 0.88f), null, false);
            return _validKioskMaterial;
        }

        private static Material GetInvalidKioskMaterial()
        {
            if (_invalidKioskMaterial == null)
                _invalidKioskMaterial = CreateTransparentMaterial(new Color(0.42f, 0.08f, 0.08f, 0.88f), null, false);
            return _invalidKioskMaterial;
        }

        private static Material GetSignMaterial()
        {
            if (_signMaterial == null)
                _signMaterial = CreateTransparentMaterial(Color.white, CreateParkingSignTexture(), false);
            return _signMaterial;
        }

        private static Material CreateTransparentMaterial(Color color, Texture texture, bool depthTest)
        {
            Shader shader = Shader.Find("Unlit/Transparent") ?? Shader.Find("Transparent/Diffuse") ?? Shader.Find("Diffuse");
            Material material = new Material(shader);
            material.hideFlags = HideFlags.HideAndDontSave;
            material.color = color;
            if (texture != null)
                material.mainTexture = texture;

            material.SetColor("_Color", color);
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            material.SetInt("_ZWrite", 0);
            material.SetInt("_ZTest", (int)(depthTest
                ? UnityEngine.Rendering.CompareFunction.LessEqual
                : UnityEngine.Rendering.CompareFunction.Always));
            material.renderQueue = depthTest ? 3000 : 5000;
            return material;
        }

        private static Texture2D CreateParkingSignTexture()
        {
            const int size = 128;
            Texture2D texture = new Texture2D(size, size, TextureFormat.ARGB32, false);
            texture.hideFlags = HideFlags.HideAndDontSave;
            Color32 clear = new Color32(0, 0, 0, 0);
            Color32 blue = new Color32(52, 112, 210, 245);
            Color32 white = new Color32(245, 248, 255, 255);
            Color32[] pixels = new Color32[size * size];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = clear;

            FillRect(pixels, size, size, 18, 18, 92, 92, blue);
            FillRect(pixels, size, size, 32, 34, 18, 62, white);
            FillRect(pixels, size, size, 50, 34, 30, 14, white);
            FillRect(pixels, size, size, 76, 42, 12, 20, white);
            FillRect(pixels, size, size, 50, 60, 28, 14, white);

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            return texture;
        }

        private static void DrawOverlayMesh(Mesh mesh, Material material, Matrix4x4 matrix)
        {
            if (mesh == null || material == null || !material.SetPass(0))
                return;

            Graphics.DrawMeshNow(mesh, matrix);
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
