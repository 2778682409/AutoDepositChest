using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewValley.Menus;
using StardewValley.Objects;

namespace AutoDepositChest
{
    /// <summary>
    /// 拦截箱子界面的点击，处理我们的自定义绑定按钮。
    /// </summary>
    [HarmonyPatch(typeof(ItemGrabMenu))]
    public class ItemGrabMenuPatch
    {
        [HarmonyPatch(nameof(ItemGrabMenu.receiveLeftClick))]
        [HarmonyPrefix]
        public static bool Prefix(ItemGrabMenu __instance, int x, int y)
        {
            var mod = ModEntry.Instance;
            if (mod == null) return true;

            // 如果当前打开了箱子界面，且按钮位置有效
            if (mod.CurrentOpenChest != null && mod.BindButtonBounds != Rectangle.Empty)
            {
                // 如果点击点在按钮范围内
                if (x >= mod.BindButtonBounds.X && x <= mod.BindButtonBounds.Right &&
                    y >= mod.BindButtonBounds.Y && y <= mod.BindButtonBounds.Bottom)
                {
                    // 执行绑定/解绑逻辑
                    mod.ToggleBindSpecificChest(mod.CurrentOpenChest);
                    // 返回 false，阻止点击穿透到游戏底层的箱子界面
                    return false;
                }
            }

            return true; // 点击在其它地方，正常放行
        }
    }
}
