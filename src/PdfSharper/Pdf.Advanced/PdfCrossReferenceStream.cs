﻿#region PDFsharp - A .NET library for processing PDF
//
// Authors:
//   Stefan Lange
//
// Copyright (c) 2005-2016 empira Software GmbH, Cologne Area (Germany)
//
// http://www.PdfSharper.com
// http://sourceforge.net/projects/pdfsharp
//
// Permission is hereby granted, free of charge, to any person obtaining a
// copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included
// in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
// THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.
#endregion

using System.Collections.Generic;
using System.Diagnostics;
using PdfSharper.Pdf.IO;
using System.IO;
using System;
using System.Linq;

namespace PdfSharper.Pdf.Advanced
{
    /// <summary>
    /// Represents a PDF cross-reference stream.
    /// </summary>
    internal sealed class PdfCrossReferenceStream : PdfTrailer  // Reference: 3.4.7  Cross-Reference Streams / Page 106
    {
        private PdfObjectStream _viableStream = null;
        private PdfObjectStream _rootStream = null;

        /// <summary>
        /// Initializes a new instance of the <see cref="PdfCrossReferenceStream"/> class.
        /// </summary>
        public PdfCrossReferenceStream(PdfDocument document, bool initialize = false)
            : base(document)
        {
#if DEBUG && CORE
            if (Internal.PdfDiagnostics.TraceXrefStreams)
            {
                Debug.WriteLine("PdfCrossReferenceStream created.");
            }
#endif

            if (initialize)
            {
                Initialize();
            }
        }

        private void Initialize()
        {
            Owner.UnderConstruction = true;
            try
            {
                PdfDictionary decodeParams = new PdfDictionary(Owner)
                {
                    IsCompact = true
                };

                decodeParams.Elements.SetInteger("/Columns", 3);
                decodeParams.Elements.SetInteger("/Predictor", 12);

                Elements.SetObject(PdfDictionary.PdfStream.Keys.DecodeParms, decodeParams);
                Elements.SetName(Keys.Type, "XRef");
                PdfArray widthesArray = new PdfArray(Owner, new PdfInteger(1), new PdfInteger(1), new PdfInteger(1));
                Elements.SetObject(Keys.W, widthesArray);

                Stream = new PdfStream(this);
            }
            finally
            {
                Owner.UnderConstruction = false;
            }
        }

        public List<CrossReferenceStreamEntry> Entries { get; private set; } = new List<CrossReferenceStreamEntry>();

        public class CrossReferenceStreamEntry
        {
            // Reference: TABLE 3.16  Entries in a cross-refernece stream / Page 109

            public uint Type;  // 0, 1, or 2.

            public uint Field2;

            public uint Field3;

            public int ObjectNumber;
        }



        internal override void AddReference(PdfReference iref)
        {
            lock (this)
            {
                if (iref == Reference)
                {
                    return;
                }

                base.AddReference(iref);

                if (!(iref.Value is PdfFormXObject) &&
                    !(iref.Value is PdfContent) &&
                    !(iref.Value is PdfObjectStream))
                {
                    AddCompressedObject(iref);
                }
                else
                {
                    AddObject(iref);
                }

                PdfArray indexArray = Elements.GetArray(Keys.Index);
                if (indexArray != null)
                {
                    indexArray.Elements[1] = new PdfInteger(Size - indexArray.Elements.GetInteger(0));
                }
            }
        }

        private void AddCompressedObject(PdfReference iref)
        {
            var viableStream = GetViableStreamCandidate();
            //find an objectstream with room            
            if (viableStream?.Reference == iref)
            {
                return;
            }

            if (viableStream == null || viableStream.Number >= 100)
            {
                PdfReference newExtendsRef = null;
                if (viableStream != null)
                {
                    newExtendsRef = viableStream.Elements.GetReference(Keys.Extends) ?? viableStream.Reference;
                }
                viableStream = new PdfObjectStream(Owner);
                _viableStream = viableStream;
                if (newExtendsRef != null)
                {
                    viableStream.Elements.SetReference(Keys.Extends, newExtendsRef);
                }
                ObjectStreams.Insert(0, viableStream);
                Owner.Internals.AddObject(viableStream);
            }

            int index = viableStream.AddObject(iref);
            Entries.Add(new CrossReferenceStreamEntry
            {
                Type = 2,
                Field2 = (uint)viableStream.ObjectNumber, //we use position from iref later
                Field3 = (uint)index,
                ObjectNumber = iref.ObjectNumber
            });

            iref.ContainingStreamID = viableStream.ObjectID;
            iref.ContainingStreamIndex = index;

        }

        private PdfObjectStream GetViableStreamCandidate()
        {
            if (_rootStream == null)
            {
                _rootStream = ObjectStreams.FirstOrDefault(os => !ObjectStreams.Any(osi => osi.Elements.GetReference(Keys.Extends)?.ObjectNumber == os.ObjectNumber));
            }

            var viableStream = _viableStream ?? _rootStream;

            if (_viableStream == null)
            {
                _viableStream = viableStream;
            }

            return viableStream;
        }

        private void AddObject(PdfReference iref)
        {
            Entries.Add(new CrossReferenceStreamEntry
            {
                Type = 1,
                Field2 = 0, //we use position from iref later
                Field3 = 0,
                ObjectNumber = iref.ObjectNumber
            });
        }

        internal override void RemoveReference(PdfReference iref)
        {
            base.RemoveReference(iref);
            PdfCrossReferenceStream.CrossReferenceStreamEntry entry = Entries.FirstOrDefault(e => e.ObjectNumber == iref.ObjectNumber);
            if (entry != null)
            {
                Entries.Remove(entry);
            }
        }

        protected override void WriteObject(PdfWriter writer)
        {
            //update object stream positions in case they have changed
            Entries = Entries.OrderBy(e => e.ObjectNumber).ToList();
            var compressedObjectReferenceLookup = Entries.ToDictionary(e => e.ObjectNumber);

            foreach (PdfObjectStream objStream in ObjectStreams)
            {
                PdfObjectStreamHeader header;
                for (int i = 0; i < objStream._header.Count; i++)
                {
                    header = objStream._header[i];

                    compressedObjectReferenceLookup[header.ObjectNumber].Field3 = (uint)i;

                }
            }


            //setup new entries stream
            PdfArray widthsArray = Elements.GetArray(Keys.W);
            int typeWidth = widthsArray.Elements.GetInteger(0);
            int field2Width = widthsArray.Elements.GetInteger(1);
            int field3Width = widthsArray.Elements.GetInteger(2);

            //do we need to increase width?
            uint maxPosition = (uint)Entries.Where(e => e.Type == 1).Select(e => XRefTable[new PdfObjectID(e.ObjectNumber)].Position).Max();
            if (maxPosition > 255 && maxPosition <= ushort.MaxValue && field2Width != 2)
            {
                field2Width = 2;
                widthsArray.Elements[1] = new PdfInteger(2);
            }
            else if (maxPosition > ushort.MaxValue && maxPosition <= 16777215 && field2Width != 3)
            {
                field2Width = 3;
                widthsArray.Elements[1] = new PdfInteger(3);
            }
            else if (maxPosition > 16777215 && maxPosition <= uint.MaxValue && field2Width != 4) //larger than 2GB files?!
            {
                field2Width = 4;
                widthsArray.Elements[1] = new PdfInteger(4);
            }
            field2Width = widthsArray.Elements.GetInteger(1);
            Stream.DecodeColumns = typeWidth + field2Width + field3Width;

            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter bw = new BinaryWriter(ms))
            {
                for (int i = 0; i < Entries.Count; i++)
                {
                    CrossReferenceStreamEntry entry = Entries[i];
                    WriteEntryValue(bw, typeWidth, entry.Type);

                    if (entry.Type == 1)
                    {
                        uint position = (uint)XRefTable[new PdfObjectID(entry.ObjectNumber)].Position;

                        WriteEntryValue(bw, field2Width, position);
                    }
                    else
                    {
                        WriteEntryValue(bw, field2Width, entry.Field2);
                    }

                    if (field3Width > 0)
                    {
                        WriteEntryValue(bw, field3Width, entry.Field3);
                    }
                }

                bw.Flush();

                Stream = new PdfStream(ms.ToArray(), this, Stream.Trailer);
                //make sure filter is reapplied
                Elements.Remove(PdfObjectStream.Keys.Filter);
                Stream.Zip();
            }

            //get groupings and update index 

            var irefs = XRefTable.AllReferences.ToList();
            int minObjectNumber = irefs.Min(ir => ir.ObjectNumber);

            //TODO: Remove when renumbering is supported
            if (minObjectNumber >= 1 && Prev == null)
            {
                irefs.Insert(0, new PdfReference(new PdfObjectID(), 0));
            }
            var xrefGroupings = irefs.OrderBy(iref => iref.ObjectNumber).GroupWhile((prev, next) => prev.ObjectNumber + 1 == next.ObjectNumber)
                .Select(anon => new
                {
                    Count = anon.Count(),
                    Irefs = anon.ToList()
                }).ToList();

            if (minObjectNumber > 1 || xrefGroupings.Count > 1)
            {
                PdfArray indexArray = new PdfArray(Owner);
                foreach (var grouping in xrefGroupings)
                {
                    indexArray.Elements.Add(new PdfInteger(grouping.Irefs.Min(ir => ir.ObjectNumber)));
                    indexArray.Elements.Add(new PdfInteger(grouping.Count));
                }

                Elements.SetObject(Keys.Index, indexArray);
            }


            Size = XRefTable._maxObjectNumber + 1;

            base.WriteObject(writer);
        }

        private void WriteEntryValue(BinaryWriter bw, int width, uint value)
        {
            switch (width)
            {
                case 1:
                    bw.Write((byte)value);
                    break;
                case 2:
                    bw.Write((byte)(value >> 8));
                    bw.Write((byte)(value));
                    break;
                case 3:
                    bw.Write((byte)(value >> 16));
                    bw.Write((byte)(value >> 8));
                    bw.Write((byte)(value));
                    break;
                case 4:
                    bw.Write((byte)(value >> 24));
                    bw.Write((byte)(value >> 16));
                    bw.Write((byte)(value >> 8));
                    bw.Write((byte)(value));
                    break;
                default:
                    throw new NotSupportedException($"Unknown cross reference width {width}");
            }
        }

        /// <summary>
        /// Predefined keys for cross-reference dictionaries.
        /// </summary>
        public new class Keys : PdfTrailer.Keys  // Reference: TABLE 3.15  Additional entries specific to a cross-refernece stream dictionary / Page 107
        {
            /// <summary>
            /// (Required) The type of PDF object that this dictionary describes;
            /// must be XRef for a cross-reference stream.
            /// </summary>
            [KeyInfo(KeyType.Name | KeyType.Required, FixedValue = "XRef")]
            public const string Type = "/Type";

            /// <summary>
            /// (Required) The number one greater than the highest object number
            /// used in this section or in any section for which this is an update.
            /// It is equivalent to the Size entry in a trailer dictionary.
            /// </summary>
            [KeyInfo(KeyType.Integer | KeyType.Required)]
            public new const string Size = "/Size";

            /// <summary>
            /// (Optional) An array containing a pair of integers for each subsection in this section.
            /// The first integer is the first object number in the subsection; the second integer
            /// is the number of entries in the subsection.
            /// The array is sorted in ascending order by object number. Subsections cannot overlap;
            /// an object number may have at most one entry in a section.
            /// Default value: [0 Size].
            /// </summary>
            [KeyInfo(KeyType.Array | KeyType.Optional)]
            public const string Index = "/Index";

            /// <summary>
            /// (Present only if the file has more than one cross-reference stream; not meaningful in 
            /// hybrid-reference files) The byte offset from the beginning of the file to the beginning
            /// of the previous cross-reference stream. This entry has the same function as the Prev 
            /// entry in the trailer dictionary.
            /// </summary>
            [KeyInfo(KeyType.Integer | KeyType.Optional)]
            public new const string Prev = "/Prev";

            /// <summary>
            /// (Required) An array of integers representing the size of the fields in a single 
            /// cross-reference entry. The table describes the types of entries and their fields.
            /// For PDF 1.5, W always contains three integers; the value of each integer is the
            /// number of bytes (in the decoded stream) of the corresponding field. For example,
            /// [1 2 1] means that the fields are one byte, two bytes, and one byte, respectively.
            /// 
            /// A value of zero for an element in the W array indicates that the corresponding field
            /// is not present in the stream, and the default value is used, if there is one. If the
            /// first element is zero, the type field is not present, and it defaults to type 1.
            /// 
            /// The sum of the items is the total length of each entry; it can be used with the
            /// Indexarray to determine the starting position of each subsection.
            /// 
            /// Note: Different cross-reference streams in a PDF file may use different values for W.
            /// 
            /// Entries in a cross-reference stream.
            /// 
            /// TYPE FIELD DESCRIPTION
            ///   0  1  The type of this entry, which must be 0. Type 0 entries define the linked list of free objects (corresponding to f entries in a cross-reference table).
            ///      2  The object number of the next free object.
            ///      3  The generation number to use if this object number is used again.
            ///   1  1  The type of this entry, which must be 1. Type 1 entries define objects that are in use but are not compressed (corresponding to n entries in a cross-reference table).
            ///      2  The byte offset of the object, starting from the beginning of the file.
            ///      3  The generation number of the object. Default value: 0.
            ///   2  1  The type of this entry, which must be 2. Type 2 entries define compressed objects.
            ///      2  The object number of the object stream in which this object is stored. (The generation number of the object stream is implicitly 0.)
            ///      3  The index of this object within the object stream.
            /// </summary>
            [KeyInfo(KeyType.Array | KeyType.Required)]
            public const string W = "/W";


            [KeyInfo(KeyType.MustBeIndirect)]
            public const string Extends = "/Extends";

            /// <summary>
            /// Gets the KeysMeta for these keys.
            /// </summary>
            public static new DictionaryMeta Meta
            {
                get { return _meta ?? (_meta = CreateMeta(typeof(Keys))); }
            }
            static DictionaryMeta _meta;
        }

        /// <summary>
        /// Gets the KeysMeta of this dictionary type.
        /// </summary>
        internal override DictionaryMeta Meta
        {
            get { return Keys.Meta; }
        }
    }
}
