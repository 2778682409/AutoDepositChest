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
        private SButton bindKey = SButton.F8;

        public override void Entry(IModHelper helper)
        {
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

        private void OnInventoryChanged(object sender, InventoryChangedEventArgs e)
        {
            if (boundChest == null) return;
            if (!e.IsLocalPlayer) return;

            // 从背包里找出新添加的物品，先移除，再存入箱子
            foreach (var item in e.Added.ToList())
            {
                if (item is Tool) continue;
                if (item.Stack <= 0) continue;

                // 在背包里找到这个物品并取出
                for (int i = 0; i < Game1.player.Items.Count; i++)
                {
                    if (Game1.player.Items[i] == item)
                    {
                        Game1.player.Items[i] = null; // 从背包移除
                        break;
                    }
                }

                // 存入箱子
                var remaining = boundChest.addItem(item);

                // 如果箱子满了，把剩余的还给玩家
                if (remaining != null && remaining.Stack > 0)
                {
                    Game1.player.addItemToInventory(remaining);
                    Game1.addHUDMessage(new HUDMessage("箱子已满，部分物品未存入"));
                }
            }
        }
    }
}
