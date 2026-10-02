using System;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Objects;
using StardewValley.Tools;

namespace AutoDepositChest
{
    public class ModEntry : Mod
    {
        private Chest boundChest = null;
        private SButton bindKey = SButton.F8;

        public override void Entry(IModHelper helper)
        {
            helper.Events.Input.ButtonPressed += OnButtonPressed;
            // 改用 ItemReceived 事件，在物品进入背包前拦截
            helper.Events.Player.ItemReceived += OnItemReceived;
        }

        private void OnButtonPressed(object sender, ButtonPressedEventArgs e)
        {
            if (e.Button != bindKey) return;

            var tile = Game1.player.GetGrabTile();
            if (Game1.currentLocation.Objects.TryGetValue(tile, out var obj) && obj is Chest chest)
            {
                if (boundChest == chest)
                {
                    boundChest = null;
                    Game1.addHUDMessage(new HUDMessage("已解绑箱子"));
                }
                else
                {
                    boundChest = chest;
                    Game1.addHUDMessage(new HUDMessage("已绑定箱子"));
                }
            }
            else
            {
                Monitor.Log("面前没有箱子。", LogLevel.Warn);
            }
        }

        private void OnItemReceived(object sender, ItemReceivedEventArgs e)
        {
            if (boundChest == null) return;
            if (e.Item is Tool) return;

            // 先把物品存入箱子
            var remaining = boundChest.addItem(e.Item);

            // 如果箱子满了，剩余物品会回到背包
            if (remaining != null && remaining.Stack > 0)
            {
                Game1.player.addItemToInventory(remaining);
                Game1.addHUDMessage(new HUDMessage("箱子已满，部分物品未存入"));
            }
        }
    }
}
