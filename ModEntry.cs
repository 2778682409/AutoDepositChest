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
        private SButton singleKey = SButton.F8;
        private SButton batchKey = SButton.F8;
        private SButton clearKey = SButton.F7;
        private int longPressThreshold = 500;
        private List<Item> lastInventory = new List<Item>();

        // 判断两个键是否相同（决定是否启用长短按）
        private bool sameKeyMode = false;

        // 按键状态
        private bool singleKeyDown = false;
        private DateTime singleKeyDownTime;
        private bool longPressActive = false;

        public override void Entry(IModHelper helper)
        {
            var config = helper.ReadConfig<ModConfig>();

            if (!Enum.TryParse(config.SingleKey, true, out singleKey))
            {
                singleKey = SButton.F8;
                Monitor.Log($"SingleKey '{config.SingleKey}' 无效，使用默认 F8。", LogLevel.Warn);
            }

            if (!Enum.TryParse(config.BatchKey, true, out batchKey))
            {
                batchKey = SButton.F8;
                Monitor.Log($"BatchKey '{config.BatchKey}' 无效，使用默认 F8。", LogLevel.Warn);
            }

            if (!Enum.TryParse(config.ClearKey, true, out clearKey))
            {
                clearKey = SButton.F7;
                Monitor.Log($"ClearKey '{config.ClearKey}' 无效，使用默认 F7。", LogLevel.Warn);
            }

            longPressThreshold = config.LongPressThreshold > 0 ? config.LongPressThreshold : 500;
            sameKeyMode = (singleKey == batchKey);

            Monitor.Log($"短按键: {singleKey}，长按键: {batchKey}，清空键: {clearKey}，长短按共用: {sameKeyMode}，长按阈值: {longPressThreshold}ms", LogLevel.Info);

            helper.Events.Input.ButtonPressed += OnButtonPressed;
            helper.Events.Input.ButtonReleased += OnButtonReleased;
            helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
            helper.Events.Display.MenuChanged += OnMenuChanged;
        }

        private void OnButtonPressed(object sender, ButtonPressedEventArgs e)
        {
            // 清空键
            if (e.Button == clearKey)
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
                return;
            }

            // 长短按共用同一个键
            if (sameKeyMode && e.Button == singleKey)
            {
                singleKeyDown = true;
                singleKeyDownTime = DateTime.Now;
                longPressActive = false;
                return;
            }

            // 两个键分开设置的情况
            if (!sameKeyMode)
            {
                // 短按键：按下即触发绑定/解绑单个
                if (e.Button == singleKey)
                {
                    ToggleBindChestUnderPlayer();
                }
                // 长按键：按下即开始批量绑定
                else if (e.Button == batchKey)
                {
                    singleKeyDown = true;
                    singleKeyDownTime = DateTime.Now;
                    longPressActive = true; // 直接进入长按模式
                    Game1.addHUDMessage(new HUDMessage("开始批量绑定，路过箱子即可自动绑定"));
                }
            }
        }

        private void OnButtonReleased(object sender, ButtonReleasedEventArgs e)
        {
            // 长按键松开：结束批量绑定
            if (!sameKeyMode && e.Button == batchKey)
            {
                Game1.addHUDMessage(new HUDMessage($"批量绑定结束（共 {boundChests.Count} 个）"));
                singleKeyDown = false;
                longPressActive = false;
                return;
            }

            // 长短按共用模式
            if (sameKeyMode && e.Button == singleKey)
            {
                var holdTime = (DateTime.Now - singleKeyDownTime).TotalMilliseconds;

                if (holdTime < longPressThreshold)
                {
                    // 短按：绑定/解绑单个
                    ToggleBindChestUnderPlayer();
                }
                else
                {
                    // 长按：批量绑定结束
                    Game1.addHUDMessage(new HUDMessage($"批量绑定结束（共 {boundChests.Count} 个）"));
                }

                singleKeyDown = false;
                longPressActive = false;
            }
        }

        private void OnMenuChanged(object sender, MenuChangedEventArgs e)
        {
            RefreshSnapshot();
        }

        private void OnUpdateTicked(object sender, UpdateTickedEventArgs e)
        {
            if (!Context.IsWorldReady) return;

            // 处理批量绑定状态
            if (singleKeyDown)
            {
                if (sameKeyMode)
                {
                    // 长短按共用：按住超过阈值才进入批量绑定
                    var holdTime = (DateTime.Now - singleKeyDownTime).TotalMilliseconds;
                    if (holdTime >= longPressThreshold)
                    {
                        if (!longPressActive)
                        {
                            longPressActive = true;
                            Game1.addHUDMessage(new HUDMessage("开始批量绑定，路过箱子即可自动绑定"));
                        }
                    }
                }

                if (longPressActive)
                {
                    TryBindChestNearPlayer();
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

        private void TryBindChestNearPlayer()
        {
            var playerTile = Game1.player.Tile;
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    var tile = new Vector2(playerTile.X + dx, playerTile.Y + dy);

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
        /// <summary>短按绑定/解绑单个箱子的按键。</summary>
        public string SingleKey { get; set; } = "F8";

        /// <summary>长按批量绑定的按键。和 SingleKey 相同时自动启用长短按检测。</summary>
        public string BatchKey { get; set; } = "F8";

        /// <summary>一键解绑全部箱子。</summary>
        public string ClearKey { get; set; } = "F7";

        /// <summary>长短按共用时的判定阈值（毫秒）。</summary>
        public int LongPressThreshold { get; set; } = 500;
    }
}
