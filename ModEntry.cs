using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
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
        private const string SaveDataKey = "bound-chests";
        public static ModEntry Instance { get; private set; }

        private static readonly HashSet<string> GeodeIds = new HashSet<string>
        {
            "535", "536", "537", "749", "275", "791", "MysteryBox", "GoldenMysteryBox"
        };

        private ModConfig Config;

        private List<Chest> boundChests = new List<Chest>();
        private SButton singleKey = SButton.F8;
        private SButton batchKey = SButton.F8;
        private SButton clearKey = SButton.F7;
        private SButton toggleKey = SButton.F6;
        private int longPressThreshold = 500;
        private List<Item> lastInventory = new List<Item>();

        private bool sameKeyMode = false;
        private bool singleKeyDown = false;
        private DateTime singleKeyDownTime;
        private bool longPressActive = false;

        private bool autoDepositEnabled = true;

        public Chest CurrentOpenChest { get; private set; } = null;
        public Rectangle BindButtonBounds { get; private set; } = Rectangle.Empty;

        public override void Entry(IModHelper helper)
        {
            Instance = this;

            var harmony = new Harmony(this.ModManifest.UniqueID);
            harmony.PatchAll();

            Config = helper.ReadConfig<ModConfig>();

            if (!Enum.TryParse(Config.SingleKey, true, out singleKey)) singleKey = SButton.F8;
            if (!Enum.TryParse(Config.BatchKey, true, out batchKey)) batchKey = SButton.F8;
            if (!Enum.TryParse(Config.ClearKey, true, out clearKey)) clearKey = SButton.F7;
            if (!Enum.TryParse(Config.ToggleKey, true, out toggleKey)) toggleKey = SButton.F6;

            longPressThreshold = Config.LongPressThreshold > 0 ? Config.LongPressThreshold : 500;
            sameKeyMode = (singleKey == batchKey);

            Monitor.Log($"短按: {singleKey}，长按: {batchKey}，清空: {clearKey}，开关: {toggleKey}，长短按共用: {sameKeyMode}", LogLevel.Info);

            helper.Events.Input.ButtonPressed += OnButtonPressed;
            helper.Events.Input.ButtonReleased += OnButtonReleased;
            helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
            helper.Events.Display.MenuChanged += OnMenuChanged;
            helper.Events.Display.RenderedActiveMenu += OnRenderedActiveMenu;
            helper.Events.GameLoop.GameLaunched += OnGameLaunched;
            helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
            helper.Events.GameLoop.Saving += OnSaving;
        }

        // ================== 绘制按钮（默认在箱子图标正下方，同宽贴边） ==================
        private void OnRenderedActiveMenu(object sender, RenderedActiveMenuEventArgs e)
        {
            if (Game1.activeClickableMenu is ItemGrabMenu grabMenu && CurrentOpenChest != null)
            {
                float uiScale = Game1.options.uiScale;
                if (uiScale <= 0) uiScale = 1f;

                // 从配置读取基准值（默认为箱子图标下方、同宽）
                int baseOffsetX = Config.BtnOffsetX;
                int baseOffsetY = Config.BtnOffsetY;
                int baseWidth = Config.BtnWidth;
                int baseHeight = Config.BtnHeight;

                // 计算实际屏幕位置和大小
                int btnX = grabMenu.xPositionOnScreen + (int)(baseOffsetX * uiScale);
                int btnY = grabMenu.yPositionOnScreen + (int)(baseOffsetY * uiScale);
                int btnW = (int)(baseWidth * uiScale);
                int btnH = (int)(baseHeight * uiScale);

                if (btnW <= 0 || btnH <= 0) return;

                BindButtonBounds = new Rectangle(btnX, btnY, btnW, btnH);

                bool isBound = boundChests.Contains(CurrentOpenChest);
                string bindButtonText = isBound ? "已绑定" : "未绑定";
                Color textColor = isBound ? Color.Green : Color.Red;

                // 绘制原版菜单框
                IClickableMenu.drawTextureBox(
                    e.SpriteBatch,
                    Game1.menuTexture,
                    new Rectangle(0, 256, 60, 60),
                    btnX, btnY, btnW, btnH,
                    Color.White,
                    1f,
                    true
                );

                // ===== 字体自适应（保证不溢出按钮） =====
                Vector2 baseTextSize = Game1.smallFont.MeasureString(bindButtonText);

                float paddingX = 10f * uiScale;   // 内边距
                float paddingY = 8f * uiScale;
                float availableW = btnW - paddingX;
                float availableH = btnH - paddingY;

                if (availableW <= 0 || availableH <= 0 || baseTextSize.X <= 0 || baseTextSize.Y <= 0)
                    return;

                float fitScaleX = availableW / baseTextSize.X;
                float fitScaleY = availableH / baseTextSize.Y;
                float textScale = Math.Min(uiScale, Math.Min(fitScaleX, fitScaleY));
                if (textScale < 0.3f) textScale = 0.3f;

                Vector2 scaledTextSize = baseTextSize * textScale;
                Vector2 textPos = new Vector2(
                    btnX + (btnW - scaledTextSize.X) / 2,
                    btnY + (btnH - scaledTextSize.Y) / 2
                );

                e.SpriteBatch.DrawString(
                    Game1.smallFont,
                    bindButtonText,
                    textPos,
                    textColor,
                    0f,
                    Vector2.Zero,
                    textScale,
                    SpriteEffects.None,
                    0.5f
                );
            }
            else
            {
                BindButtonBounds = Rectangle.Empty;
            }
        }

        // ================== 存档持久化 ==================
        private void OnSaveLoaded(object sender, SaveLoadedEventArgs e)
        {
            boundChests.Clear();
            var data = Helper.Data.ReadSaveData<ChestSaveData>(SaveDataKey);
            if (data == null) return;

            foreach (var entry in data.Entries)
            {
                var location = Game1.getLocationFromName(entry.LocationName);
                if (location == null) continue;

                var tile = new Vector2(entry.X, entry.Y);
                if (location.Objects.TryGetValue(tile, out var obj) && obj is Chest chest)
                {
                    boundChests.Add(chest);
                }
            }
            Monitor.Log($"已从存档加载 {boundChests.Count} 个绑定箱子。", LogLevel.Info);
        }

        private void OnSaving(object sender, SavingEventArgs e)
        {
            var data = new ChestSaveData();
            foreach (var chest in boundChests)
            {
                if (chest?.Location == null) continue;
                data.Entries.Add(new ChestSaveEntry
                {
                    LocationName = chest.Location.Name,
                    X = (int)chest.TileLocation.X,
                    Y = (int)chest.TileLocation.Y
                });
            }
            Helper.Data.WriteSaveData(SaveDataKey, data);
        }

        // ================== GMCM ==================
        private void OnGameLaunched(object sender, GameLaunchedEventArgs e)
        {
            var configMenu = Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
            if (configMenu == null) return;

            configMenu.Register(
                mod: ModManifest,
                reset: () =>
                {
                    Config.SingleKey = "F8"; Config.BatchKey = "F8"; Config.ClearKey = "F7"; Config.ToggleKey = "F6";
                    Config.LongPressThreshold = 500;
                    Config.BtnOffsetX = 32; Config.BtnOffsetY = 100; Config.BtnWidth = 64; Config.BtnHeight = 44;
                },
                save: () =>
                {
                    Helper.WriteConfig(Config);
                    if (Enum.TryParse(Config.SingleKey, true, out SButton s)) singleKey = s;
                    if (Enum.TryParse(Config.BatchKey, true, out SButton b)) batchKey = b;
                    if (Enum.TryParse(Config.ClearKey, true, out SButton c)) clearKey = c;
                    if (Enum.TryParse(Config.ToggleKey, true, out SButton t)) toggleKey = t;
                    longPressThreshold = Config.LongPressThreshold > 0 ? Config.LongPressThreshold : 500;
                    sameKeyMode = (singleKey == batchKey);
                }
            );

            configMenu.AddKeybind(mod: ModManifest, name: () => "绑定/解绑单个箱子", tooltip: () => "短按此键：绑定或解绑面前的单个箱子。", getValue: () => ParseKey(Config.SingleKey), setValue: value => Config.SingleKey = value.ToString());
            configMenu.AddKeybind(mod: ModManifest, name: () => "批量绑定", tooltip: () => "按住此键走路，路过箱子自动绑定。", getValue: () => ParseKey(Config.BatchKey), setValue: value => Config.BatchKey = value.ToString());
            configMenu.AddKeybind(mod: ModManifest, name: () => "一键解绑全部", tooltip: () => "按下此键，解绑所有已绑定的箱子。", getValue: () => ParseKey(Config.ClearKey), setValue: value => Config.ClearKey = value.ToString());
            configMenu.AddKeybind(mod: ModManifest, name: () => "临时关闭/开启自动存入", tooltip: () => "按下此键，临时暂停或恢复自动存入功能。", getValue: () => ParseKey(Config.ToggleKey), setValue: value => Config.ToggleKey = value.ToString());
            configMenu.AddNumberOption(mod: ModManifest, name: () => "长按判定时间（毫秒）", tooltip: () => "按住超过这个时间算长按。", getValue: () => Config.LongPressThreshold, setValue: value => Config.LongPressThreshold = value, min: 100, max: 2000, interval: 50);

            // UI 微调（默认值已经是你想要的位置，一般不用动）
            configMenu.AddSectionTitle(mod: ModManifest, text: () => "按钮位置微调");
            configMenu.AddNumberOption(mod: ModManifest, name: () => "按钮 X 坐标偏移",
                tooltip: () => "相对于箱子界面左上角的水平偏移量（默认 32，与箱子图标左边缘对齐）。",
                getValue: () => Config.BtnOffsetX, setValue: value => Config.BtnOffsetX = value,
                min: -100, max: 1000, interval: 1);
            configMenu.AddNumberOption(mod: ModManifest, name: () => "按钮 Y 坐标偏移",
                tooltip: () => "相对于箱子界面左上角的垂直偏移量（默认 100，在箱子图标下方）。",
                getValue: () => Config.BtnOffsetY, setValue: value => Config.BtnOffsetY = value,
                min: -100, max: 1000, interval: 1);
            configMenu.AddNumberOption(mod: ModManifest, name: () => "按钮宽度",
                tooltip: () => "按钮的宽度（默认 64，与箱子图标同宽）。",
                getValue: () => Config.BtnWidth, setValue: value => Config.BtnWidth = value,
                min: 20, max: 400, interval: 1);
            configMenu.AddNumberOption(mod: ModManifest, name: () => "按钮高度",
                tooltip: () => "按钮的高度（默认 44）。",
                getValue: () => Config.BtnHeight, setValue: value => Config.BtnHeight = value,
                min: 20, max: 200, interval: 1);
        }

        private SButton ParseKey(string key) => Enum.TryParse(key, true, out SButton result) ? result : SButton.F8;

        private Item GetChestIcon(Chest chest)
        {
            try { return ItemRegistry.Create(chest.QualifiedItemId, 1); }
            catch { try { return new StardewValley.Object(chest.ItemId, 1); } catch { return null; } }
        }

        // ================== 箱子界面检测 ==================
        private void OnMenuChanged(object sender, MenuChangedEventArgs e)
        {
            CurrentOpenChest = null;
            if (e.NewMenu is ItemGrabMenu grabMenu)
            {
                var sourceField = grabMenu.GetType().GetField("source", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (sourceField != null)
                {
                    var source = sourceField.GetValue(grabMenu);
                    if (source is Chest chest) CurrentOpenChest = chest;
                }
                if (CurrentOpenChest == null) CurrentOpenChest = FindChestInMenu(grabMenu);
            }
            RefreshSnapshot();
        }

        private Chest FindChestInMenu(ItemGrabMenu menu)
        {
            var fields = menu.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (var field in fields)
            {
                var value = field.GetValue(menu);
                if (value is Chest chest) return chest;
                if (value is IEnumerable enumerable && !(value is string))
                {
                    foreach (var item in enumerable) if (item is Chest c) return c;
                }
            }
            return null;
        }

        // ================== 按键处理 ==================
        private void OnButtonPressed(object sender, ButtonPressedEventArgs e)
        {
            if (e.Button == toggleKey)
            {
                autoDepositEnabled = !autoDepositEnabled;
                Game1.playSound(autoDepositEnabled ? "bigSelect" : "bigDeSelect");
                Game1.addHUDMessage(new HUDMessage(autoDepositEnabled ? "已开启自动存入" : "已暂停自动存入"));
                return;
            }

            if (e.Button == clearKey)
            {
                if (boundChests.Count == 0) Game1.addHUDMessage(new HUDMessage("当前没有绑定任何箱子"));
                else
                {
                    int count = boundChests.Count;
                    Item icon = GetChestIcon(boundChests[0]);
                    boundChests.Clear();
                    Game1.playSound("trashcan");
                    Game1.addHUDMessage(new HUDMessage($"已解绑全部箱子（共 {count} 个）") { messageSubject = icon });
                    RefreshSnapshot();
                }
                return;
            }

            if (CurrentOpenChest != null && (e.Button == singleKey || e.Button == batchKey))
            {
                ToggleBindSpecificChest(CurrentOpenChest);
                return;
            }

            if (sameKeyMode && e.Button == singleKey)
            {
                singleKeyDown = true; singleKeyDownTime = DateTime.Now; longPressActive = false; return;
            }

            if (!sameKeyMode)
            {
                if (e.Button == singleKey) ToggleBindChestUnderPlayer();
                else if (e.Button == batchKey)
                {
                    singleKeyDown = true; singleKeyDownTime = DateTime.Now; longPressActive = true;
                    Game1.addHUDMessage(new HUDMessage("开始批量绑定，路过箱子即可自动绑定"));
                }
            }
        }

        private void OnButtonReleased(object sender, ButtonReleasedEventArgs e)
        {
            if (CurrentOpenChest != null) return;
            if (!sameKeyMode && e.Button == batchKey)
            {
                Game1.addHUDMessage(new HUDMessage($"批量绑定结束（共 {boundChests.Count} 个）"));
                singleKeyDown = false; longPressActive = false; return;
            }
            if (sameKeyMode && e.Button == singleKey)
            {
                var holdTime = (DateTime.Now - singleKeyDownTime).TotalMilliseconds;
                if (holdTime < longPressThreshold) ToggleBindChestUnderPlayer();
                else Game1.addHUDMessage(new HUDMessage($"批量绑定结束（共 {boundChests.Count} 个）"));
                singleKeyDown = false; longPressActive = false;
            }
        }

        // ================== 绑定逻辑 ==================
        public void ToggleBindSpecificChest(Chest chest)
        {
            Item icon = GetChestIcon(chest);
            if (boundChests.Contains(chest))
            {
                boundChests.Remove(chest);
                Game1.playSound("cancel");
                Game1.addHUDMessage(new HUDMessage($"已解绑当前箱子（剩余 {boundChests.Count} 个）") { messageSubject = icon });
            }
            else
            {
                boundChests.Add(chest);
                Game1.playSound("coin");
                Game1.addHUDMessage(new HUDMessage($"已绑定当前箱子（共 {boundChests.Count} 个）") { messageSubject = icon });
            }
            RefreshSnapshot();
        }

        private void ToggleBindChestUnderPlayer()
        {
            var tile = Game1.player.GetGrabTile();
            if (Game1.currentLocation.Objects.TryGetValue(tile, out var obj) && obj is Chest chest) ToggleBindSpecificChest(chest);
            else Game1.addHUDMessage(new HUDMessage("面前没有箱子"));
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
                            Game1.playSound("coin");
                            Game1.addHUDMessage(new HUDMessage($"已绑定箱子（共 {boundChests.Count} 个）") { messageSubject = GetChestIcon(chest) });
                            RefreshSnapshot();
                        }
                    }
                }
            }
        }

        // ================== 主循环 ==================
        private void OnUpdateTicked(object sender, UpdateTickedEventArgs e)
        {
            if (!Context.IsWorldReady) return;
            if (CurrentOpenChest != null) { CleanupMissingChests(); RefreshSnapshot(); return; }

            if (singleKeyDown)
            {
                if (sameKeyMode)
                {
                    if ((DateTime.Now - singleKeyDownTime).TotalMilliseconds >= longPressThreshold && !longPressActive)
                    {
                        longPressActive = true; Game1.addHUDMessage(new HUDMessage("开始批量绑定，路过箱子即可自动绑定"));
                    }
                }
                if (longPressActive) TryBindChestNearPlayer();
            }

            CleanupMissingChests();
            if (!autoDepositEnabled) { RefreshSnapshot(); return; }
            if (boundChests.Count == 0) return;

            bool isGeodeMenu = Game1.activeClickableMenu is GeodeMenu;
            if (Game1.activeClickableMenu != null && !isGeodeMenu) return;

            var player = Game1.player;
            for (int i = 0; i < player.Items.Count; i++)
            {
                var item = player.Items[i];
                if (item == null || item is Tool) continue;
                if (isGeodeMenu && IsGeodeItem(item)) continue;

                if (!lastInventory.Contains(item))
                {
                    player.Items[i] = null;
                    Item remaining = item;
                    Chest targetChest = null;
                    foreach (var chest in boundChests) { if (ChestContainsItem(chest, item)) { targetChest = chest; break; } }
                    if (targetChest != null) remaining = targetChest.addItem(remaining);
                    if (remaining != null && remaining.Stack > 0)
                    {
                        foreach (var chest in boundChests) { if (remaining == null || remaining.Stack <= 0) break; if (chest == targetChest) continue; remaining = chest.addItem(remaining); }
                    }
                    if (remaining != null && remaining.Stack > 0) { player.addItemToInventory(remaining); Game1.addHUDMessage(new HUDMessage("所有绑定箱子已满，部分物品未存入")); }
                }
            }
            RefreshSnapshot();
        }

        // ================== 辅助 ==================
        private bool IsGeodeItem(Item item) => item != null && GeodeIds.Contains(item.ItemId);

        private void CleanupMissingChests()
        {
            for (int i = boundChests.Count - 1; i >= 0; i--)
            {
                var chest = boundChests[i];
                if (chest?.Location == null) { boundChests.RemoveAt(i); continue; }
                if (!chest.Location.Objects.TryGetValue(chest.TileLocation, out var obj) || obj != chest)
                {
                    boundChests.RemoveAt(i);
                    Game1.addHUDMessage(new HUDMessage("检测到已绑定箱子消失，自动解绑"));
                }
            }
        }

        private bool ChestContainsItem(Chest chest, Item item)
        {
            foreach (var slot in chest.Items) if (slot != null && slot.QualifiedItemId == item.QualifiedItemId) return true;
            return false;
        }

        private void RefreshSnapshot()
        {
            lastInventory.Clear();
            foreach (var item in Game1.player.Items) if (item != null) lastInventory.Add(item);
        }
    }

    // ================== 存档数据与配置 ==================
    public class ChestSaveData { public List<ChestSaveEntry> Entries { get; set; } = new List<ChestSaveEntry>(); }
    public class ChestSaveEntry { public string LocationName { get; set; } = ""; public int X { get; set; } public int Y { get; set; } }
    public class ModConfig
    {
        public string SingleKey { get; set; } = "F8";
        public string BatchKey { get; set; } = "F8";
        public string ClearKey { get; set; } = "F7";
        public string ToggleKey { get; set; } = "F6";
        public int LongPressThreshold { get; set; } = 500;

        // 按钮默认在箱子图标正下方、同宽贴边
        public int BtnOffsetX { get; set; } = 32;
        public int BtnOffsetY { get; set; } = 100;
        public int BtnWidth { get; set; } = 64;
        public int BtnHeight { get; set; } = 44;
    }
}
