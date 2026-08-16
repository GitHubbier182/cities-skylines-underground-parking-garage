using System;
using System.IO;
using System.Reflection;
using ColossalFramework.UI;
using UnityEngine;

namespace UndergroundParkingGarage
{
    internal static class UndergroundParkingInfoTooltip
    {
        private static readonly string[] ResourceNames =
        {
            "UndergroundParkingGarage.Resources.Tooltips.Compact.png",
            "UndergroundParkingGarage.Resources.Tooltips.Grand.png",
            "UndergroundParkingGarage.Resources.Tooltips.Civic.png"
        };

        private static readonly UITextureAtlas[] Atlases =
            new UITextureAtlas[UndergroundParkingStandaloneCatalog.VariantCount];

        public static bool Bind(
            UIButton button,
            UndergroundParkingStandaloneVariant variant)
        {
            if (button == null)
                return false;

            BuildingInfo prefab = UndergroundParkingBuildingPrefab.EnsurePrefab(variant);
            UITextureAtlas atlas = GetOrCreateAtlas(variant);
            if (prefab == null || atlas == null)
                return false;

            string spriteName = GetSpriteName(variant);
            prefab.m_InfoTooltipAtlas = atlas;
            prefab.m_InfoTooltipThumbnail = spriteName;

            // Use the stock PublicTransportPanel/GeneratedScrollPanel tooltip
            // path without recreating its sprite-population behavior.
            button.tooltipAnchor = UITooltipAnchor.Anchored;
            button.tooltipBox = GeneratedPanel.tooltipBox;
            button.tooltip = prefab.GetLocalizedTooltip();
            button.objectUserData = prefab;
            button.eventTooltipEnter += delegate
            {
                PublicTransportPanel publicTransportPanel =
                    UnityEngine.Object.FindObjectOfType<PublicTransportPanel>();
                if (publicTransportPanel != null)
                    publicTransportPanel.OnTooltipEnter(button, prefab);
            };
            return true;
        }

        private static UITextureAtlas GetOrCreateAtlas(
            UndergroundParkingStandaloneVariant variant)
        {
            int index = (int)variant;
            if (index < 0 || index >= Atlases.Length)
                return null;
            if (Atlases[index] != null)
                return Atlases[index];

            try
            {
                Assembly assembly = Assembly.GetExecutingAssembly();
                using (Stream stream = assembly.GetManifestResourceStream(ResourceNames[index]))
                {
                    if (stream == null)
                    {
                        UndergroundParkingLog.Warning(
                            "Missing embedded vanilla info-tooltip image: "
                            + ResourceNames[index]);
                        return null;
                    }

                    byte[] bytes = new byte[stream.Length];
                    int offset = 0;
                    while (offset < bytes.Length)
                    {
                        int read = stream.Read(bytes, offset, bytes.Length - offset);
                        if (read <= 0)
                            break;
                        offset += read;
                    }

                    if (offset != bytes.Length)
                        throw new EndOfStreamException("Embedded tooltip image ended early.");

                    Texture2D texture = new Texture2D(
                        SnapshotTool.tooltipWidth,
                        SnapshotTool.tooltipHeight,
                        TextureFormat.ARGB32,
                        false);
                    texture.name = GetSpriteName(variant);
                    texture.hideFlags = HideFlags.HideAndDontSave;
                    // Vanilla's UITextureAtlas.AddTextures packs pixels from
                    // the source texture, so it must remain readable until
                    // CreateThumbnailAtlas has completed.
                    if (!texture.LoadImage(bytes, false))
                    {
                        UnityEngine.Object.Destroy(texture);
                        return null;
                    }

                    texture.wrapMode = TextureWrapMode.Clamp;
                    texture.filterMode = FilterMode.Bilinear;
                    Atlases[index] = AssetImporterThumbnails.CreateThumbnailAtlas(
                        new[] { texture },
                        "UndergroundParkingGarage" + variant + "InfoTooltipAtlas");
                    return Atlases[index];
                }
            }
            catch (Exception e)
            {
                UndergroundParkingLog.Warning(
                    "Could not prepare the " + variant
                    + " vanilla info-tooltip image: " + e.Message);
                return null;
            }
        }

        private static string GetSpriteName(
            UndergroundParkingStandaloneVariant variant)
        {
            return "UndergroundParkingGarage" + variant + "InfoTooltip";
        }
    }
}
