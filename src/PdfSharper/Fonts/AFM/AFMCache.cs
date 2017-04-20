﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PdfSharper.Fonts.AFM
{
    public class AFMCache
    {
        private Dictionary<string, AFMDetails> FontMetrics { get; set; }

        private static AFMCache _instance;

        private AFMCache()
        {
            this.FontMetrics = new Dictionary<string, AFMDetails>();
        }

        public static AFMCache Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new AFMCache();
                }

                return _instance;
            }
        }

        public AFMDetails GetFontMetricsByName(string fontName)
        {
            AFMDetails fontMetric = null;

            string fontMetricSource = AFMSource.GetSourceByName(fontName);
            if (!string.IsNullOrWhiteSpace(fontMetricSource))
            {
                fontMetric = this.GetFontMetrics(fontName, fontMetricSource);
            }

            return fontMetric;
        }

        public AFMDetails GetFontMetricsByNameAndAttributes(string fontName, bool isBold, bool isItalic)
        {
            AFMDetails fontMetric = null;

            string fontMetricSource = AFMSource.GetSourceByNameAndAttributes(fontName, isBold, isItalic);
            if (!string.IsNullOrWhiteSpace(fontMetricSource))
            {
                fontMetric = this.GetFontMetrics(fontName, fontMetricSource);
            }

            return fontMetric;
        }

        private AFMDetails GetFontMetrics(string fontName, string fontMetricSource)
        {
            AFMDetails fontMetric = null;

            if (!string.IsNullOrWhiteSpace(fontName))
            {
                if (this.FontMetrics.ContainsKey(fontName))
                {
                    fontMetric = this.FontMetrics[fontName];
                }
                else if (!string.IsNullOrWhiteSpace(fontMetricSource))
                {
                    fontMetric = this.LoadFontMetric(fontName, fontMetricSource);
                }
            }

            return fontMetric;
        }

        private AFMDetails LoadFontMetric(string fontName, string fontMetricSource)
        {
            AFMDetails fontMetric = BuildFontMetrics(fontMetricSource);

            if (!this.FontMetrics.ContainsKey(fontName) && fontMetric != null && !string.IsNullOrWhiteSpace(fontMetric.FontName) && fontMetric.CharacterWidths != null && fontMetric.CharacterWidths.Any())
            {
                this.FontMetrics.Add(fontName, fontMetric);
            }
            else
            {
                fontMetric = null;
            }

            return fontMetric;
        }

        private AFMDetails BuildFontMetrics(string afm)
        {
            string fontNameLabel = "FontName ";
            string ascenderLabel = "Ascender ";
            string descenderLabel = "Descender ";
            string capHeightLabel = "CapHeight ";
            string startFontMetricsLabel = "StartFontMetrics";
            string endFontMetricsLabel = "EndFontMetrics";
            string endCharMetricsLabel = "EndCharMetrics";
            string characterLineLabel = "C ";
            string characterWidthLabel = "WX ";
            string characterLinePropertyDelimeter = " ; ";
            string fontBBoxLabel = "FontBBox ";

            AFMDetails fontMetric = null;

            using (StreamReader stream = new StreamReader(afm))
            {
                string line = stream.ReadLine();
                if (!string.IsNullOrWhiteSpace(line) && line.Contains(startFontMetricsLabel))
                {
                    bool continueReading = true;

                    int ascender = 0;
                    int descender = 0;
                    int capHeight = 0;
                    int bboxLLY = 0;
                    int bboxLLX = 0;
                    int bboxURX = 0;
                    int bboxURY = 0;
                    string fontName = string.Empty;
                    Dictionary<char, int> characterWidths = new Dictionary<char, int>();

                    while (continueReading)
                    {
                        if (line.StartsWith(characterLineLabel))
                        {
                            line.Substring(characterLineLabel.Length, line.IndexOf(characterLinePropertyDelimeter));
                            line.Substring(line.IndexOf(characterWidthLabel), line.IndexOf(" ; N"));

                            int indexValue = this.GetCharacterPropertyValue(line, characterLineLabel, characterLinePropertyDelimeter);
                            int widthValue = this.GetCharacterPropertyValue(line.Substring(line.IndexOf(characterWidthLabel)), characterWidthLabel, characterLinePropertyDelimeter);

                            if (indexValue > 0 && indexValue < 256 && widthValue >= 0)
                            {
                                characterWidths.Add((char)indexValue, widthValue);
                            }
                        }

                        if (line.StartsWith(fontNameLabel))
                        {
                            fontName = line.Substring(fontNameLabel.Length);
                        }

                        if (line.StartsWith(ascenderLabel))
                        {
                            Int32.TryParse(line.Substring(ascenderLabel.Length), out ascender);
                        }

                        if (line.StartsWith(descenderLabel))
                        {
                            Int32.TryParse(line.Substring(descenderLabel.Length), out descender);
                        }

                        if (line.StartsWith(capHeightLabel))
                        {
                            Int32.TryParse(line.Substring(capHeightLabel.Length), out capHeight);
                        }

                        if(line.StartsWith(fontBBoxLabel))
                        {
                            string[] bboxValues = line.Substring(fontBBoxLabel.Length).Split(' ');
                            Int32.TryParse(bboxValues[0], out bboxLLX);
                            Int32.TryParse(bboxValues[1], out bboxLLY);
                            Int32.TryParse(bboxValues[2], out bboxURX);
                            Int32.TryParse(bboxValues[3], out bboxURY);
                        }

                        if (line.Contains(endFontMetricsLabel) || line.Contains(endCharMetricsLabel))
                        {
                            continueReading = false;
                        }

                        if (continueReading)
                        {
                            line = stream.ReadLine();
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(fontName) && characterWidths.Count > 0)
                    {
                        fontMetric = new AFMDetails
                        {
                            FontName = fontName,
                            Ascender = ascender,
                            Descender = descender,
                            CapHeight = capHeight,
                            BBoxLLX = bboxLLX,
                            BBoxLLY = bboxLLY,
                            BBoxURX = bboxURX,
                            BBoxURY = bboxURY,
                            CharacterWidths = characterWidths
                        };
                    }
                }
            }

            return fontMetric;
        }

        private int GetCharacterPropertyValue(string line, string propertyLabel, string propertyDelimiter)
        {
            int index = 0;

            string indexString = line.Substring(propertyLabel.Length, line.IndexOf(propertyDelimiter) - propertyLabel.Length);

            if (!string.IsNullOrWhiteSpace(indexString))
            {
                Int32.TryParse(indexString, out index);
            }

            return index;
        }
    }
}
