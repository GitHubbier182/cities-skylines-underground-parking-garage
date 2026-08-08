using System.Collections.Generic;
using UnityEngine;

namespace UndergroundParkingGarage
{
    internal static class UndergroundParkingMarkGeometry
    {
        private static readonly Color ParkingBlueDisplay = new Color32(0, 102, 178, 255);
        private static readonly Color ParkingWhiteDisplay = new Color32(254, 254, 254, 255);

        // Proportions measured from the supplied standard parking-sign artwork.
        // The visible bounds are centred on the requested origin. The stem and
        // shoulders are rectilinear; only the right-hand bowl/counter arcs curve.
        private const float StemLeft = -0.4895f;
        private const float StemRight = -0.2295f;
        private const float StemBottom = -0.64f;
        private const float StemTop = 0.64f;
        private const float OuterCenterX = 0.0955f;
        private const float OuterCenterY = 0.226f;
        private const float OuterRadiusX = 0.394f;
        private const float OuterRadiusY = 0.414f;
        private const float InnerCenterX = 0.0715f;
        private const float InnerCenterY = 0.2265f;
        private const float InnerRadiusX = 0.164f;
        private const float InnerRadiusY = 0.1895f;
        private const int BowlSegments = 16;

        internal static Color ParkingBlueVertex
        {
            get { return ToUnlitVertexColor(ParkingBlueDisplay); }
        }

        internal static Color ParkingWhiteVertex
        {
            get { return ToUnlitVertexColor(ParkingWhiteDisplay); }
        }

        private static Color ToUnlitVertexColor(Color displayColor)
        {
            // Hidden/Internal-Colored writes vertex RGB directly into the active
            // render target. In the game's linear colour space, display-space
            // bytes must therefore be encoded to linear intensity first; otherwise
            // RGB 0,102,178 is gamma-lifted into the pale cyan seen in UAT.
            return QualitySettings.activeColorSpace == ColorSpace.Linear
                ? displayColor.linear
                : displayColor;
        }

        public static void AddCenteredParkingSignP(
            List<Vector3> vertices,
            List<Vector2> uvs,
            List<Color> colors,
            List<int> triangles,
            float centerX,
            float centerZ,
            float scale,
            float xSign,
            float zSign,
            float y,
            Vector2 uv)
        {
            AddParkingSignP(
                vertices, uvs, colors, triangles,
                centerX, centerZ, scale, xSign, zSign, y, uv, false);
        }

        public static void AddCenteredParkingSignPVertical(
            List<Vector3> vertices,
            List<Vector2> uvs,
            List<Color> colors,
            List<int> triangles,
            float centerX,
            float centerY,
            float scale,
            float xSign,
            float ySign,
            float z,
            Vector2 uv)
        {
            AddParkingSignP(
                vertices, uvs, colors, triangles,
                centerX, centerY, scale, xSign, ySign, z, uv, true);
        }

        private static void AddParkingSignP(
            List<Vector3> vertices,
            List<Vector2> uvs,
            List<Color> colors,
            List<int> triangles,
            float centerU,
            float centerV,
            float scale,
            float uSign,
            float vSign,
            float plane,
            Vector2 uv,
            bool vertical)
        {
            AddLocalQuad(
                vertices, uvs, colors, triangles,
                StemLeft, StemBottom,
                StemRight, StemBottom,
                StemRight, StemTop,
                StemLeft, StemTop,
                centerU, centerV, scale, uSign, vSign, plane, uv, vertical);

            float outerTop = OuterCenterY + OuterRadiusY;
            float outerBottom = OuterCenterY - OuterRadiusY;
            float innerTop = InnerCenterY + InnerRadiusY;
            float innerBottom = InnerCenterY - InnerRadiusY;

            // Flat upper and lower shoulders reproduce the standard sign's D-shaped
            // bowl and counter instead of wrapping a complete ellipse around the stem.
            AddLocalQuad(
                vertices, uvs, colors, triangles,
                StemRight, innerTop,
                InnerCenterX, innerTop,
                OuterCenterX, outerTop,
                StemRight, outerTop,
                centerU, centerV, scale, uSign, vSign, plane, uv, vertical);
            AddLocalQuad(
                vertices, uvs, colors, triangles,
                StemRight, outerBottom,
                OuterCenterX, outerBottom,
                InnerCenterX, innerBottom,
                StemRight, innerBottom,
                centerU, centerV, scale, uSign, vSign, plane, uv, vertical);

            for (int i = 0; i < BowlSegments; i++)
            {
                float angleA = -Mathf.PI * 0.5f + Mathf.PI * i / BowlSegments;
                float angleB = -Mathf.PI * 0.5f + Mathf.PI * (i + 1) / BowlSegments;
                float outerAx = OuterCenterX + Mathf.Cos(angleA) * OuterRadiusX;
                float outerAy = OuterCenterY + Mathf.Sin(angleA) * OuterRadiusY;
                float outerBx = OuterCenterX + Mathf.Cos(angleB) * OuterRadiusX;
                float outerBy = OuterCenterY + Mathf.Sin(angleB) * OuterRadiusY;
                float innerBx = InnerCenterX + Mathf.Cos(angleB) * InnerRadiusX;
                float innerBy = InnerCenterY + Mathf.Sin(angleB) * InnerRadiusY;
                float innerAx = InnerCenterX + Mathf.Cos(angleA) * InnerRadiusX;
                float innerAy = InnerCenterY + Mathf.Sin(angleA) * InnerRadiusY;
                AddLocalQuad(
                    vertices, uvs, colors, triangles,
                    outerAx, outerAy,
                    outerBx, outerBy,
                    innerBx, innerBy,
                    innerAx, innerAy,
                    centerU, centerV, scale, uSign, vSign, plane, uv, vertical);
            }
        }

        private static void AddLocalQuad(
            List<Vector3> vertices,
            List<Vector2> uvs,
            List<Color> colors,
            List<int> triangles,
            float ax, float ay,
            float bx, float by,
            float cx, float cy,
            float dx, float dy,
            float centerU,
            float centerV,
            float scale,
            float uSign,
            float vSign,
            float plane,
            Vector2 uv,
            bool vertical)
        {
            AddQuad(
                vertices, uvs, colors, triangles,
                Map(ax, ay, centerU, centerV, scale, uSign, vSign, plane, vertical),
                Map(bx, by, centerU, centerV, scale, uSign, vSign, plane, vertical),
                Map(cx, cy, centerU, centerV, scale, uSign, vSign, plane, vertical),
                Map(dx, dy, centerU, centerV, scale, uSign, vSign, plane, vertical),
                uv);
        }

        private static Vector3 Map(
            float localU,
            float localV,
            float centerU,
            float centerV,
            float scale,
            float uSign,
            float vSign,
            float plane,
            bool vertical)
        {
            float u = centerU + localU * scale * uSign;
            float v = centerV + localV * scale * vSign;
            return vertical ? new Vector3(u, v, plane) : new Vector3(u, plane, v);
        }

        private static void AddQuad(
            List<Vector3> vertices,
            List<Vector2> uvs,
            List<Color> colors,
            List<int> triangles,
            Vector3 a,
            Vector3 b,
            Vector3 c,
            Vector3 d,
            Vector2 uv)
        {
            int start = vertices.Count;
            AddVertex(vertices, uvs, colors, a, uv);
            AddVertex(vertices, uvs, colors, b, uv);
            AddVertex(vertices, uvs, colors, c, uv);
            AddVertex(vertices, uvs, colors, d, uv);
            triangles.Add(start);
            triangles.Add(start + 2);
            triangles.Add(start + 1);
            triangles.Add(start);
            triangles.Add(start + 3);
            triangles.Add(start + 2);
        }

        private static void AddVertex(
            List<Vector3> vertices,
            List<Vector2> uvs,
            List<Color> colors,
            Vector3 vertex,
            Vector2 uv)
        {
            vertices.Add(vertex);
            uvs.Add(uv);
            if (colors != null)
            {
                colors.Add(uv.x < 0.5f
                    ? ParkingBlueVertex
                    : ParkingWhiteVertex);
            }
        }
    }
}
