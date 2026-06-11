using System;
using System.Collections.Generic;
using System.Linq;
using AutoTranslation.Services;
using RimWorld;
using UnityEngine;
using Verse;

namespace AutoTranslation.UI
{
    public class Dialog_GistMerge : Window
    {
        private Dictionary<string, MergeItem> mergeItems;
        private Vector2 scrollPosition;
        private HashSet<string> selectedKeys = new HashSet<string>();
        
        private const float WindowWidth = 1000f;
        private const float WindowHeight = 700f;
        private const float RowHeight = 40f;
        
        public override Vector2 InitialSize => new Vector2(WindowWidth, WindowHeight);
        
        public class MergeItem
        {
            public string Key;
            public string LocalTranslation;
            public string RemoteTranslation;
            public bool IsNew; // 로컬에 없는 새로운 번역인지
        }
        
        public Dialog_GistMerge(Dictionary<string, string> remoteTranslations)
        {
            mergeItems = new Dictionary<string, MergeItem>();
            
            // 로컬 캐시와 비교하여 차이점 추출
            foreach (var remote in remoteTranslations)
            {
                var key = remote.Key;
                var remoteValue = remote.Value;
                
                if (TranslatorManager.CachedTranslationsV2.TryGetValue(key, out var localValue))
                {
                    // 값이 다르면 병합 대상
                    if (localValue != remoteValue)
                    {
                        mergeItems[key] = new MergeItem
                        {
                            Key = key,
                            LocalTranslation = localValue,
                            RemoteTranslation = remoteValue,
                            IsNew = false
                        };
                        selectedKeys.Add(key); // 기본적으로 모두 선택
                    }
                }
                else
                {
                    // 로컬에 없는 새로운 번역
                    mergeItems[key] = new MergeItem
                    {
                        Key = key,
                        LocalTranslation = "",
                        RemoteTranslation = remoteValue,
                        IsNew = true
                    };
                    selectedKeys.Add(key); // 기본적으로 모두 선택
                }
            }
            
            doCloseX = true;
            doCloseButton = false;
            closeOnClickedOutside = false;
            absorbInputAroundWindow = true;
        }
        
        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Small;
            
            float currentY = 0f;
            
            // Title
            Text.Font = GameFont.Medium;
            var titleRect = new Rect(0f, currentY, inRect.width, 35f);
            Widgets.Label(titleRect, "AT_Gist_MergeTitle".Translate());
            Text.Font = GameFont.Small;
            currentY += 40f;
            
            // Info
            var infoRect = new Rect(0f, currentY, inRect.width, 25f);
            Widgets.Label(infoRect, $"{"AT_Gist_MergeInfo".Translate()}: {mergeItems.Count}");
            currentY += 30f;
            
            // Buttons
            var buttonRect = new Rect(0f, currentY, inRect.width, 30f);
            var selectAllRect = new Rect(buttonRect.x, buttonRect.y, 120f, 28f);
            var deselectAllRect = new Rect(selectAllRect.xMax + 5f, buttonRect.y, 120f, 28f);
            
            if (Widgets.ButtonText(selectAllRect, "AT_Gist_SelectAll".Translate()))
            {
                selectedKeys.Clear();
                foreach (var key in mergeItems.Keys)
                {
                    selectedKeys.Add(key);
                }
            }
            
            if (Widgets.ButtonText(deselectAllRect, "AT_Gist_DeselectAll".Translate()))
            {
                selectedKeys.Clear();
            }
            
            currentY += 35f;
            
            // List
            var listRect = new Rect(0f, currentY, inRect.width, inRect.height - currentY - 50f);
            DrawMergeList(listRect);
            
            // Bottom buttons
            var bottomY = inRect.height - 45f;
            var mergeRect = new Rect(inRect.width - 160f, bottomY, 150f, 35f);
            var cancelRect = new Rect(mergeRect.x - 160f, bottomY, 150f, 35f);
            
            if (Widgets.ButtonText(mergeRect, "AT_Gist_MergeSelected".Translate()))
            {
                ApplyMerge();
                Close();
            }
            
            if (Widgets.ButtonText(cancelRect, "Cancel".Translate()))
            {
                Close();
            }
        }
        
        private void DrawMergeList(Rect rect)
        {
            if (mergeItems.Count == 0)
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(rect, "AT_Gist_NoDifferences".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                return;
            }
            
            // Column headers
            var headerRect = new Rect(rect.x, rect.y, rect.width, 25f);
            DrawColumnHeaders(headerRect);
            
            var contentRect = new Rect(rect.x, rect.y + 30f, rect.width, rect.height - 30f);
            var items = mergeItems.Values.OrderBy(x => x.Key).ToList();
            
            float totalHeight = items.Count * RowHeight;
            var viewRect = new Rect(0f, 0f, contentRect.width - 16f, totalHeight);
            
            Widgets.BeginScrollView(contentRect, ref scrollPosition, viewRect);
            
            float currentY = 0f;
            foreach (var item in items)
            {
                var rowRect = new Rect(0f, currentY, viewRect.width, RowHeight);
                
                if (Mouse.IsOver(rowRect))
                {
                    Widgets.DrawHighlight(rowRect);
                }
                
                DrawMergeRow(rowRect, item);
                currentY += RowHeight;
            }
            
            Widgets.EndScrollView();
        }
        
        private void DrawColumnHeaders(Rect rect)
        {
            var prevAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleLeft;
            
            var checkboxRect = new Rect(rect.x, rect.y, 30f, rect.height);
            var keyRect = new Rect(checkboxRect.xMax, rect.y, rect.width * 0.25f, rect.height);
            var localRect = new Rect(keyRect.xMax, rect.y, rect.width * 0.3f, rect.height);
            var remoteRect = new Rect(localRect.xMax, rect.y, rect.width * 0.3f, rect.height);
            
            Widgets.Label(keyRect, "AT_Gist_Key".Translate());
            Widgets.Label(localRect, "AT_Gist_LocalTranslation".Translate());
            Widgets.Label(remoteRect, "AT_Gist_RemoteTranslation".Translate());
            
            Widgets.DrawLineHorizontal(rect.x, rect.yMax - 1f, rect.width);
            
            Text.Anchor = prevAnchor;
        }
        
        private void DrawMergeRow(Rect rect, MergeItem item)
        {
            var checkboxRect = new Rect(rect.x + 5f, rect.y, 25f, RowHeight);
            var isSelected = selectedKeys.Contains(item.Key);
            var newIsSelected = isSelected;
            
            Widgets.Checkbox(checkboxRect.position, ref newIsSelected);
            
            if (newIsSelected != isSelected)
            {
                if (newIsSelected)
                {
                    selectedKeys.Add(item.Key);
                }
                else
                {
                    selectedKeys.Remove(item.Key);
                }
            }
            
            var keyRect = new Rect(checkboxRect.xMax + 5f, rect.y, rect.width * 0.25f - 35f, RowHeight);
            var localRect = new Rect(keyRect.xMax + 5f, rect.y, rect.width * 0.3f - 5f, RowHeight);
            var remoteRect = new Rect(localRect.xMax + 5f, rect.y, rect.width * 0.3f - 5f, RowHeight);
            
            // Key (ModId 부분만 표시)
            var colonIndex = item.Key.IndexOf(':');
            var displayKey = colonIndex > 0 && colonIndex < 50 
                ? item.Key.Substring(0, Math.Min(colonIndex, 30)) + "..." 
                : item.Key.Substring(0, Math.Min(item.Key.Length, 30));
            
            Widgets.Label(keyRect.ContractedBy(2f), displayKey);
            TooltipHandler.TipRegion(keyRect, item.Key);
            
            // Local translation
            if (item.IsNew)
            {
                GUI.color = Color.gray;
                Widgets.Label(localRect.ContractedBy(2f), "AT_Gist_NewTranslation".Translate());
                GUI.color = Color.white;
            }
            else
            {
                Widgets.Label(localRect.ContractedBy(2f), item.LocalTranslation);
            }
            TooltipHandler.TipRegion(localRect, item.LocalTranslation);
            
            // Remote translation
            GUI.color = new Color(0.5f, 1f, 0.5f); // Light green
            Widgets.Label(remoteRect.ContractedBy(2f), item.RemoteTranslation);
            GUI.color = Color.white;
            TooltipHandler.TipRegion(remoteRect, item.RemoteTranslation);
        }
        
        private void ApplyMerge()
        {
            int count = 0;
            foreach (var key in selectedKeys)
            {
                if (mergeItems.TryGetValue(key, out var item))
                {
                    // Update both caches
                    TranslationCacheManager.AddOrUpdate(key, item.RemoteTranslation);
                    TranslatorManager.CachedTranslationsV2[key] = item.RemoteTranslation;
                    count++;
                }
            }
            
            // Save to disk
            TranslationCacheManager.Save(nameof(TranslatorManager.CachedTranslationsV2));
            
            Messages.Message($"{"AT_Gist_MergeSuccess".Translate()} ({count})", MessageTypeDefOf.PositiveEvent);
        }
    }
}


