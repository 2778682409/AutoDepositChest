using System;
using System.Linq;
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
        // 改用最基础的 SButton 写法，兼容性更好
        private SButton bindKey = SButton.F8;

        public override void Entry(IModHelper helper)
        {
            // 注册事件
            helper.Events.Input.ButtonPressed += OnButtonPressed;
            helper.Events.Player.InventoryChanged += OnInventoryChanged;
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
                    Monitor.Log("已解绑箱子。", LogLevel.Info);
                }
                else
                {
                    boundChest = chest;
                    Game1.addHUDMessage(new HUDMessage("已绑定箱子"));
                    Monitor.Log("已绑定箱子。", LogLevel.Info);
                }
            }
            else
            {
                Monitor.Log("面前没有箱子。", LogLevel.Warn);
            }
        }

        private void OnInventoryChanged(object sender, InventoryChangedEventArgs e)
        {
            if (boundChest == null) return;
            if (!e.IsLocalPlayer) return;

            foreach (var item in e.Added.ToList())
            {
                if (item is Tool) continue;
                if (item.Stack <= 0) continue;

                var remaining = boundChest.addItem(item);

                if (item.Stack <= 0)
                {
                    for (int i = 0; i < Game1.player.Items.Count; i++)
                    {
                        if (Game1.player.Items[i] == item)
                        {
                            Game1.player.Items[i] = null;
                            break;
                        }
                    }
                }

                if (remaining != null && remaining.Stack > 0)
                {
                    Game1.addHUDMessage(new HUDMessage("箱子已满，部分物品未存入"));
                }
            }
        }
    }
}
