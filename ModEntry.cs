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

            Monitor.Log($"短按: {singleKey}，长按: {batchKey}，清空: {clearKey}，开关: {toggleKey}", LogLevel.Info);

            helper.Events.Input.ButtonPressed += OnButtonPressed;
            helper.Events.Input.ButtonReleased += OnButtonReleased;
            helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
            helper.Events.Display.MenuChanged += OnMenuChanged;
            helper.Events.Display.RenderedActiveMenu += OnRenderedActiveMenu;
            helper.Events.GameLoop.GameLaunched += OnGameLaunched;
            helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
            helper.Events.GameLoop.Saving += OnSaving;
        }

        // ================== 绘制按钮 ==================
        private void OnRenderedActiveMenu(object sender, RenderedActiveMenuEventArgs e)
        {
            if (Game1.activeClickableMenu is ItemGrabMenu grabMenu && CurrentOpenChest != null)
            {
                float uiScale = Game1.options.uiScale;
                if (uiScale <= 0) uiScale = 1f;

                bool isLargeChest = grabMenu.ItemsToGrabMenu != null && grabMenu.ItemsToGrabMenu.rows >= 4;

                int offsetX = isLargeChest ? Config.LargeBtnOffsetX : Config.BtnOffsetX;
                int offsetY = isLargeChest ? Config.LargeBtnOffsetY : Config.BtnOffsetY;
                int baseWidth = isLargeChest ? Config.LargeBtnWidth : Config.BtnWidth;
                int baseHeight = isLargeChest ? Config.LargeBtnHeight : Config.BtnHeight;

                int btnX = grabMenu.xPositionOnScreen + (int)(offsetX * uiScale);
                int btnY = grabMenu.yPositionOnScreen + (int)(offsetY * uiScale);
                int btnW = (int)(baseWidth * uiScale);
                int btnH = (int)(baseHeight * uiScale);

                if (btnW <= 0 || btnH <= 0) return;

                BindButtonBounds = new Rectangle(btnX, btnY, btnW, btnH);

                bool isBound = boundChests.Contains(CurrentOpenChest);
                string bindButtonText = isBound ? "已绑定" : "未绑定";
                Color textColor = isBound ? Color.Green : Color.Red;

                IClickableMenu.drawTextureBox(
                    e.SpriteBatch,
                    Game1.menuTexture,
                    new Rectangle(0, 256, 60, 60),
                    btnX, btnY, btnW, btnH,
                    Color.White,
                    1f,
                    true
                );

                bool isVertical = btnH > btnW;

                if (isVertical)
                {
                    Vector2 charSize = Game1.smallFont.MeasureString("字");
                    float paddingX = 8f * uiScale;
                    float paddingY = 6f * uiScale;
                    float availableW = btnW - paddingX;
                    float availableH = btnH - paddingY;

                    if (availableW <= 0 || availableH <= 0) return;

                    float totalTextH = charSize.Y * bindButtonText.Length;
                    float totalTextW = charSize.X;

                    float fitScaleX = availableW / totalTextW;
                    float fitScaleY = availableH / totalTextH;
                    float textScale = Math.Min(uiScale, Math.Min(fitScaleX, fitScaleY));
                    if (textScale < 0.3f) textScale = 0.3f;

                    float scaledCharH = charSize.Y * textScale;
                    float startY = btnY + (btnH - totalTextH * textScale) / 2;

                    for (int i = 0; i < bindButtonText.Length; i++)
                    {
                        string c = bindButtonText[i].ToString();
                        Vector2 cSize = Game1.smallFont.MeasureString(c) * textScale;
                        Vector2 cPos = new Vector2(btnX + (btnW - cSize.X) / 2, startY + i * scaledCharH);

                        e.SpriteBatch.DrawString(Game1.smallFont, c, cPos, textColor, 0f, Vector2.Zero, textScale, SpriteEffects.None, 0.5f);
                    }
                }
                else
                {
                    Vector2 baseTextSize = Game1.smallFont.MeasureString(bindButtonText);
                    float paddingX = 10f * uiScale;
                    float paddingY = 8f * uiScale;
                    float availableW = btnW - paddingX;
                    float availableH = btnH - paddingY;

                    if (availableW <= 0 || availableH <= 0 || baseTextSize.X <= 0 || baseTextSize.Y <= 0) return;

                    float fitScaleX = availableW / baseTextSize.X;
                    float fitScaleY = availableH / baseTextSize.Y;
                    float textScale = Math.Min(uiScale, Math.Min(fitScaleX, fitScaleY));
                    if (textScale < 0.3f) textScale = 0.3f;

                    Vector2 scaledTextSize = baseTextSize * textScale;
                    Vector2 textPos = new Vector2(btnX + (btnW - scaledTextSize.X) / 2, btnY + (btnH - scaledTextSize.Y) / 2);

                    e.SpriteBatch.DrawString(Game1.smallFont, bindButtonText, textPos, textColor, 0f, Vector2.Zero, textScale, SpriteEffects.None, 0.5f);
                }
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
                    Config.SingleKey = "F8"; Config.BatchKey = "F8";
                    Config.ClearKey = "F7"; Config.ToggleKey = "F6";
                    Config.LongPressThreshold = 500;

                    Config.BtnOffsetX = -49; Config.BtnOffsetY = 71;
                    Config.BtnWidth = 55; Config.BtnHeight = 120;

                    Config.LargeBtnOffsetX = -49; Config.LargeBtnOffsetY = 71;
                    Config.LargeBtnWidth = 55; Config.LargeBtnHeight = 120;
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

            configMenu.AddKeybind(mod: ModManifest, name: () => "绑定/解绑单个箱子", getValue: () => ParseKey(Config.SingleKey), setValue: value => Config.SingleKey = value.ToString());
            configMenu.AddKeybind(mod: ModManifest, name: () => "批量绑定", getValue: () => ParseKey(Config.BatchKey), setValue: value => Config.BatchKey = value.ToString());
            configMenu.AddKeybind(mod: ModManifest, name: () => "一键解绑全部", getValue: () => ParseKey(Config.ClearKey), setValue: value => Config.ClearKey = value.ToString());
            configMenu.AddKeybind(mod: ModManifest, name: () => "临时关闭/开启自动存入", getValue: () => ParseKey(Config.ToggleKey), setValue: value => Config.ToggleKey = value.ToString());
            configMenu.AddNumberOption(mod: ModManifest, name: () => "长按判定时间（毫秒）", getValue: () => Config.LongPressThreshold, setValue: value => Config.LongPressThreshold = value, min: 100, max: 2000, interval: 50);

            configMenu.AddSectionTitle(mod: ModManifest, text: () => "普通箱子按钮位置");
            configMenu.AddTextOption(mod: ModManifest, name: () => "X 偏移", getValue: () => Config.BtnOffsetX.ToString(), setValue: value => { if (int.TryParse(value, out int v)) Config.BtnOffsetX = v; });
            configMenu.AddTextOption(mod: ModManifest, name: () => "Y 偏移", getValue: () => Config.BtnOffsetY.ToString(), setValue: value => { if (int.TryParse(value, out int v)) Config.BtnOffsetY = v; });
            configMenu.AddTextOption(mod: ModManifest, name: () => "宽度", getValue: () => Config.BtnWidth.ToString(), setValue: value => { if (int.TryParse(value, out int v)) Config.BtnWidth = v; });
            configMenu.AddTextOption(mod: ModManifest, name: () => "高度", getValue: () => Config.BtnHeight.ToString(), setValue: value => { if (int.TryParse(value, out int v)) Config.BtnHeight = v; });

            configMenu.AddSectionTitle(mod: ModManifest, text: () => "大箱子按钮位置");
            configMenu.AddTextOption(mod: ModManifest, name: () => "X 偏移", getValue: () => Config.LargeBtnOffsetX.ToString(), setValue: value => { if (int.TryParse(value, out int v)) Config.LargeBtnOffsetX = v; });
            configMenu.AddTextOption(mod: ModManifest, name: () => "Y 偏移", getValue: () => Config.LargeBtnOffsetY.ToString(), setValue: value => { if (int.TryParse(value, out int v)) Config.LargeBtnOffsetY = v; });
            configMenu.AddTextOption(mod: ModManifest, name: () => "宽度", getValue: () => Config.LargeBtnWidth.ToString(), setValue: value => { if (int.TryParse(value, out int v)) Config.LargeBtnWidth = v; });
            configMenu.AddTextOption(mod: ModManifest, name: () => "高度", getValue: () => Config.LargeBtnHeight.ToString(), setValue: value => { if (int.TryParse(value, out int v)) Config.LargeBtnHeight = v; });
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

    // 箱子界面打开时，暂停自动存入
    if (CurrentOpenChest != null)
    {
        CleanupMissingChests();
        RefreshSnapshot();
        return;
    }

    // 处理批量绑定按键的长按逻辑
    if (singleKeyDown)
    {
        if (sameKeyMode)
        {
            if ((DateTime.Now - singleKeyDownTime).TotalMilliseconds >= longPressThreshold && !longPressActive)
            {
                longPressActive = true;
                Game1.addHUDMessage(new HUDMessage("开始批量绑定，路过箱子即可自动绑定"));
            }
        }
        if (longPressActive) TryBindChestNearPlayer();
    }

    CleanupMissingChests();

    if (!autoDepositEnabled) { RefreshSnapshot(); return; }
    if (boundChests.Count == 0) return;

    // ===== 任何菜单打开时都暂停自动存入（晶球界面除外） =====
    if (Game1.activeClickableMenu != null && !(Game1.activeClickableMenu is GeodeMenu))
    {
        RefreshSnapshot();
        return;
    }

    bool isGeodeMenu = Game1.activeClickableMenu is GeodeMenu;

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

        public int BtnOffsetX { get; set; } = -49;
        public int BtnOffsetY { get; set; } = 71;
        public int BtnWidth { get; set; } = 55;
        public int BtnHeight { get; set; } = 120;

        public int LargeBtnOffsetX { get; set; } = -49;
        public int LargeBtnOffsetY { get; set; } = 71;
        public int LargeBtnWidth { get; set; } = 55;
        public int LargeBtnHeight { get; set; } = 120;
    }
}
