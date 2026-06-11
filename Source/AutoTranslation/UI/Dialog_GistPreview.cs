using System;
using AutoTranslation.Services;
using RimWorld;
using UnityEngine;
using Verse;

namespace AutoTranslation.UI
{
    public class Dialog_GistPreview : Window
    {
        private TranslationMetadata metadata;
        private Action onConfirm;
        
        private const float WindowWidth = 500f;
        private const float WindowHeight = 300f;
        
        public override Vector2 InitialSize => new Vector2(WindowWidth, WindowHeight);
        
        public Dialog_GistPreview(TranslationMetadata metadata, Action onConfirm)
        {
            this.metadata = metadata;
            this.onConfirm = onConfirm;
            
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
            Widgets.Label(titleRect, "AT_Gist_PreviewTitle".Translate());
            Text.Font = GameFont.Small;
            currentY += 40f;
            
            // Metadata 정보 표시
            var infoRect = new Rect(20f, currentY, inRect.width - 40f, inRect.height - currentY - 50f);
            
            var prevAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.UpperLeft;
            
            float lineHeight = 25f;
            float infoY = infoRect.y;
            
            Widgets.Label(new Rect(infoRect.x, infoY, infoRect.width, lineHeight), 
                $"{"AT_Gist_TargetLanguage".Translate()}: {metadata.TargetLanguage}");
            infoY += lineHeight + 5f;
            
            Widgets.Label(new Rect(infoRect.x, infoY, infoRect.width, lineHeight), 
                $"{"AT_Gist_Translator".Translate()}: {metadata.TranslatorName}");
            infoY += lineHeight + 5f;
            
            if (!string.IsNullOrEmpty(metadata.TranslatorModel) && metadata.TranslatorModel != "Unknown")
            {
                Widgets.Label(new Rect(infoRect.x, infoY, infoRect.width, lineHeight), 
                    $"{"AT_Gist_Model".Translate()}: {metadata.TranslatorModel}");
                infoY += lineHeight + 5f;
            }
            
            Widgets.Label(new Rect(infoRect.x, infoY, infoRect.width, lineHeight), 
                $"{"AT_Gist_Date".Translate()}: {metadata.Date}");
            infoY += lineHeight + 5f;
            
            Widgets.Label(new Rect(infoRect.x, infoY, infoRect.width, lineHeight), 
                $"{"AT_Gist_TranslationCount".Translate()}: {metadata.TranslationCount}");
            
            Text.Anchor = prevAnchor;
            
            // Buttons
            var buttonY = inRect.height - 40f;
            var confirmRect = new Rect(inRect.width - 160f, buttonY, 150f, 35f);
            var cancelRect = new Rect(confirmRect.x - 160f, buttonY, 150f, 35f);
            
            if (Widgets.ButtonText(confirmRect, "AT_Gist_Confirm".Translate()))
            {
                onConfirm?.Invoke();
                Close();
            }
            
            if (Widgets.ButtonText(cancelRect, "Cancel".Translate()))
            {
                Close();
            }
        }
    }
}


