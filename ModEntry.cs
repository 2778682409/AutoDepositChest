using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
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
        private List<Item> lastInventory = new List<Item>();

        public override void Entry(IModHelper helper)
        {
            helper.Events.Input.ButtonPressed += OnButtonPressed;
            helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
            helper.Events.Display.MenuChanged += OnMenuChanged;
        }

        private void OnButtonPressed(object sender, ButtonPressedEventArgs e)
        {
            if (e.Button != bindKey) return;

            var tile = Game1.player.GetGrabTile();
            if (Game1.currentLocation.Objects.TryGetValue(tile, out var obj) && obj is Chest chest)
            {
                if (boundChests.Contains(chest))
                {
                    boundChests.Remove(chest);
                    Game1.addHUDMessage(new HUDMessage($"已解绑箱子（剩余 {boundChests.Count} 个）", 2)
                    {
                        messageSubject = new ChestSprite(chest, 0.75f)
                    });
                }
                else
                {
                    boundChests.Add(chest);
                    Game1.addHUDMessage(new HUDMessage($"已绑定箱子（共 {boundChests.Count} 个）", 2)
                    {
                        messageSubject = new ChestSprite(chest, 0.75f)
                    });
                }

                RefreshSnapshot();
            }
            else
            {
                Monitor.Log("面前没有箱子。", LogLevel.Warn);
            }
        }

        private void OnMenuChanged(object sender, MenuChangedEventArgs e)
        {
            RefreshSnapshot();
        }

        private void OnUpdateTicked(object sender, UpdateTickedEventArgs e)
        {
            if (boundChests.Count == 0) return;
            if (!Context.IsWorldReady) return;
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

    /// <summary>自定义的 HUD 显示对象，用于控制箱子贴图的缩放比例。</summary>
    public class ChestSprite : Item
    {
        private readonly Chest chest;
        private readonly float scale;

        public ChestSprite(Chest chest, float scale)
        {
            this.chest = chest;
            this.scale = scale;
            // 赋一个空白的 Name，避免 HUD 渲染文字
            this.Name = "";
            this.DisplayName = "";
            this.Category = -999;
            this.ParentSheetIndex = 0;
            // 复制箱子的贴图信息
            this.ItemId = chest.ItemId;
            this.QualifiedItemId = chest.QualifiedItemId;
        }

        public override void drawInMenu(SpriteBatch spriteBatch, Vector2 location, float scaleSize, float transparency, float layerDepth, StackDrawType drawStackNumber, Color color, bool drawShadow)
        {
            // 用我们自己指定的缩放比例绘制
            float finalScale = scaleSize * this.scale * 4f;
            chest.draw(spriteBatch, (int)location.X, (int)location.Y, finalScale, transparency, layerDepth, color);
        }
    }
}
