using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using AutoTranslation.Services;
using RimWorld;
using UnityEngine;
using Verse;

namespace AutoTranslation.UI
{
    public class Dialog_BulkReplace : Window
    {
        private string filterPattern = "";
        private string findPattern = "";
        private string replacePattern = "";
        private bool useRegex = true;
        private bool caseSensitive = false;
        
        private Vector2 scrollPosition;
        private List<ReplacePreviewItem> previewItems = new List<ReplacePreviewItem>();
        private bool previewNeedsRefresh = true;
        
        private const float WindowWidth = 900f;
        private const float WindowHeight = 700f;
        private const float ContentMargin = 10f;
        private const float RowHeight = 30f;
        private const float PreviewRowHeight = 60f;
        
        public override Vector2 InitialSize => new Vector2(WindowWidth, WindowHeight);
        
        public Dialog_BulkReplace()
        {
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
            Widgets.Label(titleRect, "AT_BulkReplace_Title".Translate());
            Text.Font = GameFont.Small;
            currentY += 40f;
            
            // Filter Section
            var filterLabel = new Rect(0f, currentY, 150f, RowHeight);
            Widgets.Label(filterLabel, "AT_BulkReplace_Filter".Translate() + ":");
            
            var filterRect = new Rect(160f, currentY, inRect.width - 160f, RowHeight);
            filterPattern = Widgets.TextField(filterRect, filterPattern);
            TooltipHandler.TipRegion(filterRect, "AT_BulkReplace_FilterTooltip".Translate());
            currentY += RowHeight + 5f;
            
            // Find Section
            var findLabel = new Rect(0f, currentY, 150f, RowHeight);
            Widgets.Label(findLabel, "AT_BulkReplace_Find".Translate() + ":");
            
            var findRect = new Rect(160f, currentY, inRect.width - 160f, RowHeight);
            var newFindPattern = Widgets.TextField(findRect, findPattern);
            if (newFindPattern != findPattern)
            {
                findPattern = newFindPattern;
                previewNeedsRefresh = true;
            }
            TooltipHandler.TipRegion(findRect, "AT_BulkReplace_FindTooltip".Translate());
            currentY += RowHeight + 5f;
            
            // Replace Section
            var replaceLabel = new Rect(0f, currentY, 150f, RowHeight);
            Widgets.Label(replaceLabel, "AT_BulkReplace_Replace".Translate() + ":");
            
            var replaceRect = new Rect(160f, currentY, inRect.width - 160f, RowHeight);
            var newReplacePattern = Widgets.TextField(replaceRect, replacePattern);
            if (newReplacePattern != replacePattern)
            {
                replacePattern = newReplacePattern;
                previewNeedsRefresh = true;
            }
            TooltipHandler.TipRegion(replaceRect, "AT_BulkReplace_ReplaceTooltip".Translate());
            currentY += RowHeight + 5f;
            
            // Options
            var regexCheckRect = new Rect(160f, currentY, 200f, RowHeight);
            var newUseRegex = useRegex;
            Widgets.CheckboxLabeled(regexCheckRect, "AT_BulkReplace_UseRegex".Translate(), ref newUseRegex);
            if (newUseRegex != useRegex)
            {
                useRegex = newUseRegex;
                previewNeedsRefresh = true;
            }
            
            var caseCheckRect = new Rect(370f, currentY, 200f, RowHeight);
            var newCaseSensitive = caseSensitive;
            Widgets.CheckboxLabeled(caseCheckRect, "AT_BulkReplace_CaseSensitive".Translate(), ref newCaseSensitive);
            if (newCaseSensitive != caseSensitive)
            {
                caseSensitive = newCaseSensitive;
                previewNeedsRefresh = true;
            }
            currentY += RowHeight + 10f;
            
            // Preview Button
            var previewButtonRect = new Rect(160f, currentY, 150f, RowHeight);
            if (Widgets.ButtonText(previewButtonRect, "AT_BulkReplace_Preview".Translate()))
            {
                RefreshPreview();
            }
            currentY += RowHeight + 10f;
            
            // Preview List Header
            var previewHeaderRect = new Rect(0f, currentY, inRect.width, 25f);
            Widgets.Label(previewHeaderRect, $"{"AT_BulkReplace_PreviewResults".Translate()} ({previewItems.Count})");
            currentY += 30f;
            
            // Preview List
            var previewListRect = new Rect(0f, currentY, inRect.width, inRect.height - currentY - 50f);
            DrawPreviewList(previewListRect);
            currentY = inRect.height - 45f;
            
            // Bottom Buttons
            var replaceAllRect = new Rect(inRect.width - 320f, currentY, 150f, 35f);
            if (Widgets.ButtonText(replaceAllRect, "AT_BulkReplace_ReplaceAll".Translate()))
            {
                ApplyReplaceAll();
            }
            
            var cancelRect = new Rect(inRect.width - 160f, currentY, 150f, 35f);
            if (Widgets.ButtonText(cancelRect, "Cancel".Translate()))
            {
                Close();
            }
        }
        
        private void DrawPreviewList(Rect rect)
        {
            if (previewNeedsRefresh)
            {
                RefreshPreview();
            }
            
            if (previewItems.Count == 0)
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(rect, "AT_BulkReplace_NoMatches".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                return;
            }
            
            float totalHeight = previewItems.Count * PreviewRowHeight;
            var viewRect = new Rect(0f, 0f, rect.width - 16f, totalHeight);
            
            Widgets.BeginScrollView(rect, ref scrollPosition, viewRect);
            
            float currentY = 0f;
            foreach (var item in previewItems)
            {
                var rowRect = new Rect(0f, currentY, viewRect.width, PreviewRowHeight);
                DrawPreviewRow(rowRect, item);
                currentY += PreviewRowHeight;
            }
            
            Widgets.EndScrollView();
        }
        
        private void DrawPreviewRow(Rect rect, ReplacePreviewItem item)
        {
            // Background
            if (Mouse.IsOver(rect))
            {
                Widgets.DrawHighlight(rect);
            }
            
            Widgets.DrawBox(rect);
            
            var innerRect = rect.ContractedBy(5f);
            float currentY = innerRect.y;
            
            // Mod ID
            var modIdRect = new Rect(innerRect.x, currentY, innerRect.width, 18f);
            GUI.color = Color.gray;
            Widgets.Label(modIdRect, $"[{item.ModId}]");
            GUI.color = Color.white;
            currentY += 18f;
            
            // Original -> New (side by side)
            var beforeRect = new Rect(innerRect.x, currentY, innerRect.width / 2 - 20f, 20f);
            Widgets.Label(beforeRect, item.OriginalTranslation);
            
            var arrowRect = new Rect(innerRect.x + innerRect.width / 2 - 15f, currentY, 30f, 20f);
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(arrowRect, "→");
            Text.Anchor = TextAnchor.UpperLeft;
            
            var afterRect = new Rect(innerRect.x + innerRect.width / 2 + 15f, currentY, innerRect.width / 2 - 20f, 20f);
            GUI.color = new Color(0.5f, 1f, 0.5f); // Light green
            Widgets.Label(afterRect, item.NewTranslation);
            GUI.color = Color.white;
        }
        
        private void RefreshPreview()
        {
            previewItems.Clear();
            previewNeedsRefresh = false;
            
            if (string.IsNullOrEmpty(findPattern))
            {
                return;
            }
            
            try
            {
                var cache = TranslationCacheManager.Instance.Cache;
                Regex filterRegex = null;
                Regex findRegex = null;
                
                // Prepare filter regex
                if (!string.IsNullOrEmpty(filterPattern))
                {
                    try
                    {
                        filterRegex = new Regex(filterPattern, RegexOptions.Compiled);
                    }
                    catch
                    {
                        // Invalid filter regex, skip filtering
                    }
                }
                
                // Prepare find regex
                var regexOptions = caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
                if (useRegex)
                {
                    findRegex = new Regex(findPattern, regexOptions | RegexOptions.Compiled);
                }
                
                foreach (var entry in cache)
                {
                    var key = entry.Key;
                    var translation = entry.Value;
                    
                    // Apply filter (on key: modId + original text)
                    if (filterRegex != null && !filterRegex.IsMatch(key))
                    {
                        continue;
                    }
                    
                    // Apply find pattern (on translation)
                    string newTranslation;
                    if (useRegex)
                    {
                        if (!findRegex.IsMatch(translation))
                        {
                            continue;
                        }
                        newTranslation = findRegex.Replace(translation, replacePattern);
                    }
                    else
                    {
                        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
                        if (translation.IndexOf(findPattern, comparison) < 0)
                        {
                            continue;
                        }
                        // Simple string replace
                        newTranslation = ReplaceString(translation, findPattern, replacePattern, comparison);
                    }
                    
                    if (newTranslation != translation)
                    {
                        var colonIndex = key.IndexOf(':');
                        var modId = colonIndex > 0 ? key.Substring(0, colonIndex) : "Unknown";
                        
                        previewItems.Add(new ReplacePreviewItem
                        {
                            Key = key,
                            ModId = modId,
                            OriginalTranslation = translation,
                            NewTranslation = newTranslation
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"{AutoTranslation.LogPrefix}Error generating preview: {ex.Message}");
            }
        }
        
        private void ApplyReplaceAll()
        {
            if (previewItems.Count == 0)
            {
                Messages.Message("AT_BulkReplace_NoChanges".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }
            
            try
            {
                int count = 0;
                var updated = new List<KeyValuePair<string, string>>();
                foreach (var item in previewItems)
                {
                    TranslationCacheManager.AddOrUpdate(item.Key, item.NewTranslation);

                    // Also update TranslatorManager cache if exists
                    if (TranslatorManager.CachedTranslationsV2.ContainsKey(item.Key))
                    {
                        TranslatorManager.CachedTranslationsV2[item.Key] = item.NewTranslation;
                    }

                    updated.Add(new KeyValuePair<string, string>(item.Key, item.NewTranslation));
                    count++;
                }

                // Save once
                TranslationCacheManager.Save(nameof(TranslatorManager.CachedTranslationsV2));

                // Apply to the live game immediately - no restart needed
                if (InjectionManager.ReapplyTranslations(updated) > 0)
                {
                    Settings.ResetDefCaches();
                }

                Messages.Message($"{"AT_BulkReplace_Success".Translate()} ({count})", MessageTypeDefOf.PositiveEvent);
                
                // Refresh Settings UI if open
                Settings.RefreshEditorFilter();
                
                Close();
            }
            catch (Exception ex)
            {
                Log.Error($"{AutoTranslation.LogPrefix}Error applying bulk replace: {ex.Message}");
                Messages.Message("AT_BulkReplace_Error".Translate(), MessageTypeDefOf.RejectInput);
            }
        }
        
        private string ReplaceString(string source, string find, string replace, StringComparison comparison)
        {
            int index = 0;
            var result = source;
            
            while ((index = result.IndexOf(find, index, comparison)) >= 0)
            {
                result = result.Substring(0, index) + replace + result.Substring(index + find.Length);
                index += replace.Length;
            }
            
            return result;
        }
        
        private class ReplacePreviewItem
        {
            public string Key;
            public string ModId;
            public string OriginalTranslation;
            public string NewTranslation;
        }
    }
}


