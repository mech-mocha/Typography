﻿//MIT, 2016-2017, WinterDev
using System;
using System.IO;
using System.Collections.Generic;
//
using PixelFarm.Agg;
using Typography.OpenFont;
using Typography.TextLayout;
using Typography.Rendering;

namespace PixelFarm.Drawing.Fonts
{


    class DevVxsTextPrinter : DevTextPrinterBase
    {

        GlyphPathBuilder _glyphPathBuilder;
        GlyphLayout _glyphLayout = new GlyphLayout();
        Dictionary<Typeface, GlyphPathBuilder> _cacheGlyphPathBuilders = new Dictionary<Typeface, GlyphPathBuilder>();
        List<GlyphPlan> _outputGlyphPlans = new List<GlyphPlan>();
        //
        HintedVxsGlyphCollection hintGlyphCollection = new HintedVxsGlyphCollection();
        VertexStorePool _vxsPool = new VertexStorePool();
        GlyphTranslatorToVxs _tovxs = new GlyphTranslatorToVxs();
        Typeface _currentTypeface;


        public DevVxsTextPrinter()
        {

        }
        public override Typeface Typeface
        {
            get
            {
                return _currentTypeface;
            }
            set
            {

                if (_currentTypeface == value) return;
                // 
                //switch to another font              
                if (_glyphPathBuilder != null && !_cacheGlyphPathBuilders.ContainsKey(value))
                {
                    //store current typeface to cache
                    _cacheGlyphPathBuilders[_currentTypeface] = _glyphPathBuilder;
                }
                //reset
                _currentTypeface = value;
                _glyphPathBuilder = null;
                if (value == null) return;
                //----------------------------
                //check if we have this in cache ?
                //if we don't have it, this _currentTypeface will set to null ***                  
                _cacheGlyphPathBuilders.TryGetValue(_currentTypeface, out _glyphPathBuilder);

                if (_glyphPathBuilder == null)
                {
                    _glyphPathBuilder = new GlyphPathBuilder(value);
                }
                OnFontSizeChanged();
            }
        }
        public override GlyphLayout GlyphLayoutMan
        {
            get
            {
                return _glyphLayout;
            }
        }


        protected override void OnFontSizeChanged()
        {
            //update some font matrix property  
            if (_glyphPathBuilder != null)
            {

                Typeface currentTypeface = _glyphPathBuilder.Typeface;
                float pointToPixelScale = currentTypeface.CalculateToPixelScaleFromPointSize(this.FontSizeInPoints);
                this.FontAscendingPx = currentTypeface.Ascender * pointToPixelScale;
                this.FontDescedingPx = currentTypeface.Descender * pointToPixelScale;
                this.FontLineGapPx = currentTypeface.LineGap * pointToPixelScale;
                this.FontLineSpacingPx = FontAscendingPx - FontDescedingPx + FontLineGapPx;
            }
        }

        public CanvasPainter TargetCanvasPainter { get; set; }
        public override void DrawCaret(float xpos, float ypos)
        {
            CanvasPainter p = this.TargetCanvasPainter;
            PixelFarm.Drawing.Color prevColor = p.StrokeColor;
            p.StrokeColor = PixelFarm.Drawing.Color.Red;
            p.Line(xpos, ypos, xpos, ypos + this.FontAscendingPx);
            p.StrokeColor = prevColor;
        }
        public override void DrawString(char[] textBuffer, int startAt, int len, float xpos, float ypos)
        {
            UpdateGlyphLayoutSettings();
            _outputGlyphPlans.Clear();
            _glyphLayout.GenerateGlyphPlans(textBuffer, startAt, len, _outputGlyphPlans, null);
            DrawFromGlyphPlans(_outputGlyphPlans, xpos, ypos);

        }

        VertexStore GetGlyphOrCreateNew(ushort glyphIndex)
        {
            VertexStore glyphVxs;
            if (!hintGlyphCollection.TryGetCacheGlyph(glyphIndex, out glyphVxs))
            {
                //if not found then create new glyph vxs and cache it
                _glyphPathBuilder.BuildFromGlyphIndex(glyphIndex, this.FontSizeInPoints);
                //-----------------------------------
                _tovxs.Reset();
                _glyphPathBuilder.ReadShapes(_tovxs);

                //TODO: review here,
                //float pxScale = _glyphPathBuilder.GetPixelScale();
                glyphVxs = new VertexStore();
                _tovxs.WriteOutput(glyphVxs, _vxsPool);
                //
                hintGlyphCollection.RegisterCachedGlyph(glyphIndex, glyphVxs);
            }
            return glyphVxs;
        }

        public override void DrawFromGlyphPlans(List<GlyphPlan> glyphPlanList, int startAt, int len, float xpos, float ypos)
        {
            CanvasPainter canvasPainter = this.TargetCanvasPainter;
            Typeface typeface = _glyphPathBuilder.Typeface;
            //3. layout glyphs with selected layout technique
            //TODO: review this again, we should use pixel?

            float fontSizePoint = this.FontSizeInPoints;
            float scale = typeface.CalculateToPixelScaleFromPointSize(fontSizePoint);


            //4. render each glyph
            Color originalFillColor = canvasPainter.FillColor;
            float ox = canvasPainter.OriginX;
            float oy = canvasPainter.OriginY;
            int endBefore = startAt + len;

            Typography.OpenFont.Tables.COLR COLR = _currentTypeface.COLRTable;
            Typography.OpenFont.Tables.CPAL CPAL = _currentTypeface.CPALTable;
            bool hasColorGlyphs = (COLR != null) && (CPAL != null);

            //---------------------------------------------------
            //consider use cached glyph, to increase performance 
            hintGlyphCollection.SetCacheInfo(typeface, fontSizePoint, this.HintTechnique);
            //---------------------------------------------------

            if (!hasColorGlyphs)
            {
                //no color info, simple render each glyph plan
                foreach (GlyphPlan glyphPlan in glyphPlanList)
                {
                    canvasPainter.SetOrigin(glyphPlan.x * scale + xpos, glyphPlan.y * scale + ypos);
                    canvasPainter.Fill(GetGlyphOrCreateNew(glyphPlan.glyphIndex));
                }
            }
            else
            {
                //has some color
                foreach (GlyphPlan glyphPlan in glyphPlanList)
                {
                    canvasPainter.SetOrigin(glyphPlan.x * scale + xpos, glyphPlan.y * scale + ypos);
                    //
                    ushort colorLayerStart;
                    if (COLR.LayerIndices.TryGetValue(glyphPlan.glyphIndex, out colorLayerStart))
                    {
                        //we found color info for this glyph 
                        ushort colorLayerCount = COLR.LayerCounts[glyphPlan.glyphIndex];
                        byte r, g, b, a;
                        for (int c = colorLayerStart; c < colorLayerStart + colorLayerCount; ++c)
                        {
                            ushort gIndex = COLR.GlyphLayers[c];

                            int palette = 0; // FIXME: assume palette 0 for now 
                            CPAL.GetColor(
                                CPAL.Palettes[palette] + COLR.GlyphPalettes[c], //index
                                out r, out g, out b, out a);
                            canvasPainter.FillColor = new Color(r, g, b);//? a component
                            canvasPainter.Fill(GetGlyphOrCreateNew(gIndex));
                        }
                    }
                    else
                    {
                        //no color info for this glyph
                        canvasPainter.Fill(GetGlyphOrCreateNew(glyphPlan.glyphIndex));
                    }
                }
            }

            //restore prev origin
            canvasPainter.SetOrigin(ox, oy);
            canvasPainter.FillColor = originalFillColor;
        }

        void UpdateGlyphLayoutSettings()
        {

            //2.1 
            _glyphPathBuilder.SetHintTechnique(this.HintTechnique);
            //2.2
            _glyphLayout.Typeface = this.Typeface;
            _glyphLayout.ScriptLang = this.ScriptLang;
            _glyphLayout.PositionTechnique = this.PositionTechnique;
            _glyphLayout.EnableLigature = this.EnableLigature;
            //3.
            //color...
        }

    }

}