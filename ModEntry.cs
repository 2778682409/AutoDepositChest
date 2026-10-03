using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Objects;
using StardewValley.Tools;
using GenericModConfigMenu;

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

        private bool sameKeyMode = false;
        private bool singleKeyDown = false;
        private DateTime singleKeyDownTime;
        private bool longPressActive = false;

        private Chest currentOpenChest = null;

        public override void Entry(IModHelper helper)
        {
            var config = helper.ReadConfig<ModConfig>();

            if (!Enum.TryParse(config.SingleKey, true, out singleKey)) singleKey = SButton.F8;
            if (!Enum.TryParse(config.BatchKey, true, out batchKey)) batchKey = SButton.F8;
            if (!Enum.TryParse(config.ClearKey, true, out clearKey)) clearKey = SButton.F7;

            longPressThreshold = config.LongPressThreshold > 0 ? config.LongPressThreshold : 500;
            sameKeyMode = (singleKey == batchKey);

            Monitor.Log($"短按键: {singleKey}，长按键: {batchKey}，清空键: {clearKey}，长短按共用: {sameKeyMode}", LogLevel.Info);

            helper.Events.Input.ButtonPressed += OnButtonPressed;
            helper.Events.Input.ButtonReleased += OnButtonReleased;
            helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
            helper.Events.Display.MenuChanged += OnMenuChanged;
            helper.Events.GameLoop.GameLaunched += OnGameLaunched;
        }

        private void OnGameLaunched(object sender, GameLaunchedEventArgs e)
        {
            var configMenu = Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
            if (configMenu == null)
            {
                Monitor.Log("未检测到 Generic Mod Config Menu，跳过注册。", LogLevel.Info);
                return;
            }

            var config = Helper.ReadConfig<ModConfig>();

            configMenu.Register(
                mod: ModManifest,
                reset: () =>
                {
                    config.SingleKey = "F8";
                    config.BatchKey = "F8";
                    config.ClearKey = "F7";
                    config.LongPressThreshold = 500;
                },
                save: () =>
                {
                    Helper.WriteConfig(config);
                    if (Enum.TryParse(config.SingleKey, true, out SButton s)) singleKey = s;
                    if (Enum.TryParse(config.BatchKey, true, out SButton b)) batchKey = b;
                    if (Enum.TryParse(config.ClearKey, true, out SButton c)) clearKey = c;
                    longPressThreshold = config.LongPressThreshold > 0 ? config.LongPressThreshold : 500;
                    sameKeyMode = (singleKey == batchKey);
                }
            );

            configMenu.AddKeybind(mod: ModManifest, name: () => "绑定/解绑单个箱子",
                tooltip: () => "短按此键：绑定或解绑面前的单个箱子。打开箱子界面时也可直接绑定当前箱子。",
                getValue: () => ParseKey(config.SingleKey),
                setValue: value => config.SingleKey = value.ToString());

            configMenu.AddKeybind(mod: ModManifest, name: () => "批量绑定",
                tooltip: () => "按住此键走路，路过箱子自动绑定。",
                getValue: () => ParseKey(config.BatchKey),
                setValue: value => config.BatchKey = value.ToString());

            configMenu.AddKeybind(mod: ModManifest, name: () => "一键解绑全部箱子",
                tooltip: () => "按下此键，解绑所有已绑定的箱子。",
                getValue: () => ParseKey(config.ClearKey),
                setValue: value => config.ClearKey = value.ToString());

            configMenu.AddNumberOption(mod: ModManifest, name: () => "长按判定时间（毫秒）",
                tooltip: () => "按住超过这个时间算长按。",
                getValue: () => config.LongPressThreshold,
                setValue: value => config.LongPressThreshold = value,
                min: 100, max: 2000, interval: 50);
        }

        private SButton ParseKey(string key)
        {
            if (Enum.TryParse(key, true, out SButton result)) return result;
            return SButton.F8;
        }

        /// <summary>根据箱子自身物品 ID 创建对应的 Item，用于 HUD 显示正确贴图。</summary>
        private Item GetChestIcon(Chest chest)
        {
            try
            {
                return ItemRegistry.Create(chest.QualifiedItemId, 1);
            }
            catch
            {
                try { return new StardewValley.Object(chest.ItemId, 1); }
                catch { return null; }
            }
        }

        private void OnMenuChanged(object sender, MenuChangedEventArgs e)
        {
            currentOpenChest = null;

            if (e.NewMenu is ItemGrabMenu grabMenu)
            {
                var sourceField = grabMenu.GetType().GetField("source",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                if (sourceField != null)
                {
                    var source = sourceField.GetValue(grabMenu);
                    if (source is Chest chest)
                    {
                        currentOpenChest = chest;
                        Monitor.Log($"打开箱子界面，绑定状态: {(boundChests.Contains(chest) ? "已绑定" : "未绑定")}", LogLevel.Info);
                    }
                }

                if (currentOpenChest == null)
                {
                    currentOpenChest = FindChestInMenu(grabMenu);
                    if (currentOpenChest != null)
                    {
                        Monitor.Log($"通过遍历菜单找到箱子，绑定状态: {(boundChests.Contains(currentOpenChest) ? "已绑定" : "未绑定")}", LogLevel.Info);
                    }
                }
            }

            RefreshSnapshot();
        }

        private Chest FindChestInMenu(ItemGrabMenu menu)
        {
            var fields = menu.GetType().GetFields(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            foreach (var field in fields)
            {
                var value = field.GetValue(menu);
                if (value is Chest chest)
                {
                    return chest;
                }

                if (value is IEnumerable enumerable && !(value is string))
                {
                    foreach (var item in enumerable)
                    {
                        if (item is Chest c)
                        {
                            return c;
                        }
                    }
                }
            }

            return null;
        }

        private void OnButtonPressed(object sender, ButtonPressedEventArgs e)
        {
            if (e.Button == clearKey)
            {
                if (boundChests.Count == 0)
                {
                    Game1.addHUDMessage(new HUDMessage("当前没有绑定任何箱子"));
                }
                else
                {
                    int count = boundChests.Count;
                    Item icon = GetChestIcon(boundChests[0]); // 用第一个箱子的图标
                    boundChests.Clear();
                    Game1.addHUDMessage(new HUDMessage($"已解绑全部箱子（共 {count} 个）")
                    {
                        messageSubject = icon
                    });
                    RefreshSnapshot();
                }
                return;
            }

            if (currentOpenChest != null && (e.Button == singleKey || e.Button == batchKey))
            {
                ToggleBindSpecificChest(currentOpenChest);
                return;
            }

            if (sameKeyMode && e.Button == singleKey)
            {
                singleKeyDown = true;
                singleKeyDownTime = DateTime.Now;
                longPressActive = false;
                return;
            }

            if (!sameKeyMode)
            {
                if (e.Button == singleKey)
                {
                    ToggleBindChestUnderPlayer();
                }
                else if (e.Button == batchKey)
                {
                    singleKeyDown = true;
                    singleKeyDownTime = DateTime.Now;
                    longPressActive = true;
                    Game1.addHUDMessage(new HUDMessage("开始批量绑定，路过箱子即可自动绑定"));
                }
            }
        }

        private void OnButtonReleased(object sender, ButtonReleasedEventArgs e)
        {
            if (currentOpenChest != null) return;

            if (!sameKeyMode && e.Button == batchKey)
            {
                Game1.addHUDMessage(new HUDMessage($"批量绑定结束（共 {boundChests.Count} 个）"));
                singleKeyDown = false;
                longPressActive = false;
                return;
            }

            if (sameKeyMode && e.Button == singleKey)
            {
                var holdTime = (DateTime.Now - singleKeyDownTime).TotalMilliseconds;
                if (holdTime < longPressThreshold)
                {
                    ToggleBindChestUnderPlayer();
                }
                else
                {
                    Game1.addHUDMessage(new HUDMessage($"批量绑定结束（共 {boundChests.Count} 个）"));
                }
                singleKeyDown = false;
                longPressActive = false;
            }
        }

        private void ToggleBindSpecificChest(Chest chest)
        {
            Item icon = GetChestIcon(chest);

            if (boundChests.Contains(chest))
            {
                boundChests.Remove(chest);
                Game1.addHUDMessage(new HUDMessage($"已解绑当前箱子（剩余 {boundChests.Count} 个）")
                {
                    messageSubject = icon
                });
            }
            else
            {
                boundChests.Add(chest);
                Game1.addHUDMessage(new HUDMessage($"已绑定当前箱子（共 {boundChests.Count} 个）")
                {
                    messageSubject = icon
                });
            }

            RefreshSnapshot();
        }

        private void OnUpdateTicked(object sender, UpdateTickedEventArgs e)
        {
            if (!Context.IsWorldReady) return;

            if (currentOpenChest != null)
            {
                CleanupMissingChests();
                return;
            }

            if (singleKeyDown)
            {
                if (sameKeyMode)
                {
                    var holdTime = (DateTime.Now - singleKeyDownTime).TotalMilliseconds;
                    if (holdTime >= longPressThreshold && !longPressActive)
                    {
                        longPressActive = true;
                        Game1.addHUDMessage(new HUDMessage("开始批量绑定，路过箱子即可自动绑定"));
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
                ToggleBindSpecificChest(chest);
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
                            Item icon = GetChestIcon(chest);
                            Game1.addHUDMessage(new HUDMessage($"已绑定箱子（共 {boundChests.Count} 个）")
                            {
                                messageSubject = icon
                            });
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
        public string SingleKey { get; set; } = "F8";
        public string BatchKey { get; set; } = "F8";
        public string ClearKey { get; set; } = "F7";
        public int LongPressThreshold { get; set; } = 500;
    }
}
