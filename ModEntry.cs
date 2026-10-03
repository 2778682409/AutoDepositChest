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
        private List<Chest> boundChests = new List<Chest>();
        private SButton bindKey = SButton.F8;
        private SButton clearKey = SButton.F7;
        private List<Item> lastInventory = new List<Item>();

        // 长按判定：按下按键后超过这个毫秒数，视为长按
        private const int LongPressThreshold = 350;

        // 按键当前状态
        private bool bindKeyDown = false;
        private int bindKeyHoldTime = 0;
        private bool longPressTriggered = false;

        public override void Entry(IModHelper helper)
        {
            var config = helper.ReadConfig<ModConfig>();

            if (Enum.TryParse(config.BindKey, true, out SButton parsedBind))
                bindKey = parsedBind;
            else
                Monitor.Log($"BindKey '{config.BindKey}' 无效，使用默认 F8。", LogLevel.Warn);

            if (Enum.TryParse(config.ClearKey, true, out SButton parsedClear))
                clearKey = parsedClear;
            else
                Monitor.Log($"ClearKey '{config.ClearKey}' 无效，使用默认 F7。", LogLevel.Warn);

            helper.Events.Input.ButtonPressed += OnButtonPressed;
            helper.Events.Input.ButtonReleased += OnButtonReleased;
            helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
            helper.Events.Display.MenuChanged += OnMenuChanged;
        }

        private void OnButtonPressed(object sender, ButtonPressedEventArgs e)
        {
            if (e.Button == bindKey)
            {
                bindKeyDown = true;
                bindKeyHoldTime = 0;
                longPressTriggered = false;
            }
            else if (e.Button == clearKey)
            {
                if (boundChests.Count == 0)
                {
                    Game1.addHUDMessage(new HUDMessage("当前没有绑定任何箱子"));
                }
                else
                {
                    int count = boundChests.Count;
                    boundChests.Clear();
                    Game1.addHUDMessage(new HUDMessage($"已解绑全部箱子（共 {count} 个）"));
                    RefreshSnapshot();
                }
            }
        }

        private void OnButtonReleased(object sender, ButtonReleasedEventArgs e)
        {
            if (e.Button == bindKey)
            {
                // 松开按键：如果没触发长按，说明是短按 → 绑定/解绑单个
                if (!longPressTriggered)
                {
                    ToggleBindChestUnderPlayer();
                }

                bindKeyDown = false;
                bindKeyHoldTime = 0;
                longPressTriggered = false;
            }
        }

        private void OnMenuChanged(object sender, MenuChangedEventArgs e)
        {
            RefreshSnapshot();
        }

        private void OnUpdateTicked(object sender, UpdateTickedEventArgs e)
        {
            if (!Context.IsWorldReady) return;

            // 处理按键长按计时
            if (bindKeyDown)
            {
                bindKeyHoldTime += (int)(e.Ticks * (1000.0 / 60.0));

                // 如果按住超过阈值，进入长按状态（批量绑定）
                if (!longPressTriggered && bindKeyHoldTime >= LongPressThreshold)
                {
                    longPressTriggered = true;
                    Game1.addHUDMessage(new HUDMessage("开始批量绑定，路过箱子即可自动绑定"));
                }

                // 长按状态下，持续检测脚下的箱子
                if (longPressTriggered)
                {
                    TryBindChestUnderPlayer();
                }
            }

            CleanupMissingChests();

            if (boundChests.Count == 0) return;
            if (Game1.activeClickableMenu != null) return;

            var player = Game1.player;
            for (int i = 0; i < player.Items.Count; i++)
            {
                var item = player.Items[i];
                if (item == null) continue;
                if (item is Tool) continue;

                if (!lastInventory.Contains(item))
                {
                    player.Items[i] = null;

                    Item remaining = item;
                    Chest targetChest = null;

                    foreach (var chest in boundChests)
                    {
                        if (ChestContainsItem(chest, item))
                        {
                            targetChest = chest;
                            break;
                        }
                    }

                    if (targetChest != null)
                    {
                        remaining = targetChest.addItem(remaining);
                    }

                    if (remaining != null && remaining.Stack > 0)
                    {
                        foreach (var chest in boundChests)
                        {
                            if (remaining == null || remaining.Stack <= 0) break;
                            if (chest == targetChest) continue;
                            remaining = chest.addItem(remaining);
                        }
                    }

                    if (remaining != null && remaining.Stack > 0)
                    {
                        player.addItemToInventory(remaining);
                        Game1.addHUDMessage(new HUDMessage("所有绑定箱子已满，部分物品未存入"));
                    }
                }
            }

            RefreshSnapshot();
        }

        /// <summary>短按：绑定/解绑脚下的单个箱子（切换）。</summary>
        private void ToggleBindChestUnderPlayer()
        {
            var tile = Game1.player.GetGrabTile();

            if (Game1.currentLocation.Objects.TryGetValue(tile, out var obj) && obj is Chest chest)
            {
                if (boundChests.Contains(chest))
                {
                    boundChests.Remove(chest);
                    Game1.addHUDMessage(new HUDMessage($"已解绑箱子（剩余 {boundChests.Count} 个）"));
                }
                else
                {
                    boundChests.Add(chest);
                    Game1.addHUDMessage(new HUDMessage($"已绑定箱子（共 {boundChests.Count} 个）"));
                }

                RefreshSnapshot();
            }
            else
            {
                Game1.addHUDMessage(new HUDMessage("面前没有箱子"));
            }
        }

        /// <summary>长按：持续绑定脚下的箱子（不触发解绑）。</summary>
        private void TryBindChestUnderPlayer()
        {
            var tile = Game1.player.GetGrabTile();

            if (Game1.currentLocation.Objects.TryGetValue(tile, out var obj) && obj is Chest chest)
            {
                if (!boundChests.Contains(chest))
                {
                    boundChests.Add(chest);
                    Game1.addHUDMessage(new HUDMessage($"已绑定箱子（共 {boundChests.Count} 个）"));
                    RefreshSnapshot();
                }
            }
        }

        private void CleanupMissingChests()
        {
            for (int i = boundChests.Count - 1; i >= 0; i--)
            {
                var chest = boundChests[i];
                var tile = chest.TileLocation;
                var location = chest.Location;

                if (location == null)
                {
                    boundChests.RemoveAt(i);
                    continue;
                }

                if (!location.Objects.TryGetValue(tile, out var obj) || obj != chest)
                {
                    boundChests.RemoveAt(i);
                    Game1.addHUDMessage(new HUDMessage("检测到已绑定箱子消失，自动解绑"));
                }
            }
        }

        private bool ChestContainsItem(Chest chest, Item item)
        {
            foreach (var slot in chest.Items)
            {
                if (slot != null && slot.QualifiedItemId == item.QualifiedItemId)
                {
                    return true;
                }
            }
            return false;
        }

        private void RefreshSnapshot()
        {
            lastInventory.Clear();
            foreach (var item in Game1.player.Items)
            {
                if (item != null) lastInventory.Add(item);
            }
        }
    }

    public class ModConfig
    {
        /// <summary>绑定/解绑单个箱子（短按），或批量绑定（长按）。</summary>
        public string BindKey { get; set; } = "F8";

        /// <summary>一键解绑全部箱子。</summary>
        public string ClearKey { get; set; } = "F7";
    }
}
