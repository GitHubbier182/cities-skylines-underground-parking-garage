using ColossalFramework.UI;
using UnityEngine;

namespace UndergroundParkingGarage
{
    internal static class UIHelpers
    {
        public static UIButton AddButton(UIComponent parent, string text, MouseEventHandler onClick)
        {
            UIButton button = parent.AddUIComponent<UIButton>();
            button.text = text;
            button.width = 190f;
            button.height = 34f;
            button.textScale = 0.82f;
            button.normalBgSprite = "ButtonMenu";
            button.hoveredBgSprite = "ButtonMenuHovered";
            button.pressedBgSprite = "ButtonMenuPressed";
            button.disabledBgSprite = "ButtonMenuDisabled";
            button.eventClick += onClick;
            return button;
        }

        public static UILabel AddLabel(UIComponent parent, string text, float scale)
        {
            UILabel label = parent.AddUIComponent<UILabel>();
            label.text = text;
            label.textScale = scale;
            label.autoSize = false;
            label.wordWrap = true;
            return label;
        }
    }
}
