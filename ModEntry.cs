using System;
using System.Collections.Generic;
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
        // 记录上一次背包里物品的快照
        private List<Item> lastInventory = new List<Item>();

        public override void Entry(IModHelper helper)
        {
            helper.Events.Input.ButtonPressed += OnButtonPressed;
            helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
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

        private void OnUpdateTicked(object sender, UpdateTickedEventArgs e)
        {
            if (boundChest == null) return;
            if (!Context.IsWorldReady) return;

            var player = Game1.player;
            // 遍历当前背包
            for (int i = 0; i < player.Items.Count; i++)
            {
                var item = player.Items[i];
                if (item == null) continue;
                if (item is Tool) continue;

                // 如果这个物品不在上一次的快照里，说明是新拾取的
                if (!lastInventory.Contains(item))
                {
                    // 从背包移除
                    player.Items[i] = null;

                    // 存入箱子
                    var remaining = boundChest.addItem(item);
                    if (remaining != null && remaining.Stack > 0)
                    {
                        player.addItemToInventory(remaining);
                        Game1.addHUDMessage(new HUDMessage("箱子已满，部分物品未存入"));
                    }
                }
            }

            // 更新快照
            lastInventory.Clear();
            foreach (var item in player.Items)
            {
                if (item != null) lastInventory.Add(item);
            }
        }
    }
}
