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

using System;
using System.Diagnostics;
using System.Collections;
using System.Collections.Generic;
using PdfSharper.Pdf.IO;
using System.Linq;

namespace PdfSharper.Pdf.Advanced
{
    /// <summary>
    /// Represents the cross-reference table of a PDF document. 
    /// It contains all indirect objects of a document.
    /// </summary>
    internal sealed class PdfCrossReferenceTable  // Must not be derive from PdfObject.
    {
        public PdfCrossReferenceTable(PdfDocument document)
        {
            _document = document;
        }
        readonly PdfDocument _document;

        /// <summary>
        /// Represents the relation between PdfObjectID and PdfReference for a PdfDocument.
        /// </summary>
        public Dictionary<PdfObjectID, PdfReference> ObjectTable = new Dictionary<PdfObjectID, PdfReference>();
        private PdfReference[] _allReferences;

        internal bool IsUnderConstruction
        {
            get { return _isUnderConstruction; }
            set { _isUnderConstruction = value; }
        }
        bool _isUnderConstruction;

        /// <summary>
        /// Adds a cross reference entry to the table. Used when parsing the trailer.
        /// </summary>
        public void Add(PdfReference iref)
        {
            if (iref.ObjectID.IsEmpty)
                iref.ObjectID = new PdfObjectID(GetNewObjectNumber());

            if (ObjectTable.ContainsKey(iref.ObjectID))
                throw new InvalidOperationException("Object already in table.");

            ObjectTable.Add(iref.ObjectID, iref);
            _allReferences = null;
            _maxObjectNumber = Math.Max(_maxObjectNumber, iref.ObjectNumber);
        }

        /// <summary>
        /// Adds a PdfObject to the table.
        /// </summary>
        public void Add(PdfObject value)
        {
            if (value.Owner == null)
                value.Document = _document;
            else
                Debug.Assert(value.Owner == _document);

            if (value.ObjectID.IsEmpty)
            {
                value.SetObjectID(GetNewObjectNumber(), 0);
                var writeableTrailer = _document.GetWritableTrailer(value.ObjectID);
                if (writeableTrailer != null && !ReferenceEquals(writeableTrailer.XRefTable, _document._irefTable) && !writeableTrailer.XRefTable.Contains(value.ObjectID))
                {
                    writeableTrailer.AddReference(value.Reference);
                }
            }

            if (ObjectTable.ContainsKey(value.ObjectID))
                throw new InvalidOperationException("Object already in table.");

            ObjectTable.Add(value.ObjectID, value.Reference);
        }

        public void Remove(PdfReference iref)
        {
            ObjectTable.Remove(iref.ObjectID);
            _allReferences = null;
        }

        /// <summary>
        /// Gets a cross reference entry from an object identifier.
        /// Returns null if no object with the specified ID exists in the object table.
        /// </summary>
        public PdfReference this[PdfObjectID objectID]
        {
            get
            {
                PdfReference iref;
                ObjectTable.TryGetValue(objectID, out iref);
                return iref;
            }
        }

        /// <summary>
        /// Indicates whether the specified object identifier is in the table.
        /// </summary>
        public bool Contains(PdfObjectID objectID)
        {
            return ObjectTable.ContainsKey(objectID);
        }

        /// <summary>
        /// Returns the next free object number.
        /// </summary>
        public int GetNewObjectNumber()
        {
            // New objects are numbered consecutively. If a document is imported, maxObjectNumber is
            // set to the highest object number used in the document.
            return ++_maxObjectNumber;
        }
        internal int _maxObjectNumber;

        /// <summary>
        /// Writes the xref section in pdf stream.
        /// </summary>
        internal void WriteObject(PdfWriter writer)
        {
            writer.WriteRaw("xref\r\n");

            PdfReference[] irefs = AllReferences;

            var xrefGroupings = irefs.OrderBy(iref => iref.ObjectNumber).GroupWhile((prev, next) => prev.ObjectNumber + 1 == next.ObjectNumber)
                .Select(anon => new
                {
                    Count = anon.Count(),
                    Irefs = anon.ToList()
                }).ToList();

            if (irefs.Min(ir => ir.ObjectNumber) > 1)
            {
                writer.WriteRaw("0 1\r\n");
                writer.WriteRaw(String.Format("{0:0000000000} {1:00000} {2}\r\n", 0, 65535, "f"));
            }

            foreach (var xrefGroup in xrefGroupings)
            {
                int count = xrefGroup.Count;
                int startingObjectNumber = xrefGroup.Irefs.FirstOrDefault().ObjectNumber;

                if (startingObjectNumber == 1)
                {
                    writer.WriteRaw(String.Format("0 {0}\r\n", count + 1));
                    writer.WriteRaw(String.Format("{0:0000000000} {1:00000} {2}\r\n", 0, 65535, "f"));
                }
                else
                {
                    writer.WriteRaw(String.Format("{0} {1}\r\n", startingObjectNumber, count));
                }

                for (int idx = 0; idx < count; idx++)
                {
                    PdfReference iref = xrefGroup.Irefs[idx];
                    // Acrobat is very pedantic; it must be exactly 20 bytes per line.
                    writer.WriteRaw(String.Format("{0:0000000000} {1:00000} {2}\r\n", iref.Position, iref.GenerationNumber, "n"));
                }
            }

        }

        /// <summary>
        /// Gets an array of all object identifiers. For debugging purposes only.
        /// </summary>
        internal PdfObjectID[] AllObjectIDs
        {
            get
            {
                ICollection collection = ObjectTable.Keys;
                PdfObjectID[] objectIDs = new PdfObjectID[collection.Count];
                collection.CopyTo(objectIDs, 0);
                return objectIDs;
            }
        }

        /// <summary>
        /// Gets an array of all cross references ordered ascendingly by their object identifier.
        /// </summary>
        internal PdfReference[] AllReferences
        {
            get
            {
                if (_allReferences == null)
                {
                    _allReferences = ObjectTable.Values.OrderBy(v => v, PdfReference.Comparer).ToArray();
                }

                return _allReferences;
            }
        }

        internal void HandleOrphanedReferences()
        { }

        /// <summary>
        /// Removes all objects that cannot be reached from the trailer.
        /// Returns the number of removed objects.
        /// </summary>
        internal int Compact()
        {
            // TODO: remove PdfBooleanObject, PdfIntegerObject etc.
            int removed = ObjectTable.Count;
            //CheckConsistence();
            // We can only compact the last trailer, if at all
            PdfReference[] irefs = PdfTraversalUtility.TransitiveClosure(_document._trailer).Select(kvp => kvp.Key).ToArray();

#if DEBUG
            // Have any two objects the same ID?
            Dictionary<int, int> ids = new Dictionary<int, int>();
            foreach (PdfObjectID objID in ObjectTable.Keys)
            {
                ids.Add(objID.ObjectNumber, 0);
            }

            // Have any two irefs the same value?
            //Dictionary<int, int> ids = new Dictionary<int, int>();
            ids.Clear();
            foreach (PdfReference iref in ObjectTable.Values)
            {
                ids.Add(iref.ObjectNumber, 0);
            }

            //
            Dictionary<PdfReference, int> refs = new Dictionary<PdfReference, int>();
            foreach (PdfReference iref in irefs)
            {
                refs.Add(iref, 0);
            }
            foreach (PdfReference value in ObjectTable.Values)
            {
                if (!refs.ContainsKey(value))
                    value.GetType();
            }

            foreach (PdfReference iref in ObjectTable.Values)
            {
                if (iref.Value == null)
                    GetType();
                Debug.Assert(iref.Value != null);
            }

            foreach (PdfReference iref in irefs)
            {
                if (!ObjectTable.ContainsKey(iref.ObjectID))
                    GetType();
                Debug.Assert(ObjectTable.ContainsKey(iref.ObjectID));

                if (iref.Value == null)
                    GetType();
                Debug.Assert(iref.Value != null);
            }
#endif

            _maxObjectNumber = 0;
            ObjectTable.Clear();
            foreach (PdfReference iref in irefs)
            {
                ObjectTable.Add(iref.ObjectID, iref);
            }
            //CheckConsistence();
            removed -= ObjectTable.Count;
            return removed;
        }

        /// <summary>
        /// Renumbers the objects starting at 1.
        /// </summary>
        internal void Renumber()
        {
            //CheckConsistence();
            PdfReference[] irefs = AllReferences;
            ObjectTable.Clear();
            // Give all objects a new number.
            int count = irefs.Length;
            for (int idx = 0; idx < count; idx++)
            {
                PdfReference iref = irefs[idx];
#if DEBUG_
                if (iref.ObjectNumber == 1108)
                    GetType();
#endif
                iref.ObjectID = new PdfObjectID(idx + 1);
                // Rehash with new number.
                ObjectTable.Add(iref.ObjectID, iref);
            }
            _maxObjectNumber = count;
            //CheckConsistence();
        }

        /// <summary>
        /// Checks the logical consistence for debugging purposes (useful after reconstruction work).
        /// </summary>
        [Conditional("DEBUG_")]
        public void CheckConsistence()
        {
            Dictionary<PdfReference, object> ht1 = new Dictionary<PdfReference, object>();
            foreach (PdfReference iref in ObjectTable.Values)
            {
                Debug.Assert(!ht1.ContainsKey(iref), "Duplicate iref.");
                Debug.Assert(iref.Value != null);
                ht1.Add(iref, null);
            }

            Dictionary<PdfObjectID, object> ht2 = new Dictionary<PdfObjectID, object>();
            foreach (PdfReference iref in ObjectTable.Values)
            {
                Debug.Assert(!ht2.ContainsKey(iref.ObjectID), "Duplicate iref.");
                ht2.Add(iref.ObjectID, null);
            }

            ICollection collection = ObjectTable.Values;
            int count = collection.Count;
            PdfReference[] irefs = new PdfReference[count];
            collection.CopyTo(irefs, 0);
#if true
            for (int i = 0; i < count; i++)
                for (int j = 0; j < count; j++)
                    if (i != j)
                    {
                        Debug.Assert(ReferenceEquals(irefs[i].Document, _document));
                        Debug.Assert(irefs[i] != irefs[j]);
                        Debug.Assert(!ReferenceEquals(irefs[i], irefs[j]));
                        Debug.Assert(!ReferenceEquals(irefs[i].Value, irefs[j].Value));
                        Debug.Assert(!Equals(irefs[i].ObjectID, irefs[j].Value.ObjectID));
                        Debug.Assert(irefs[i].ObjectNumber != irefs[j].Value.ObjectNumber);
                        Debug.Assert(ReferenceEquals(irefs[i].Document, irefs[j].Document));
                        GetType();
                    }
#endif
        }

        ///// <summary>
        ///// The garbage collector for PDF objects.
        ///// </summary>
        //public sealed class GC
        //{
        //  PdfXRefTable xrefTable;
        //
        //  internal GC(PdfXRefTable xrefTable)
        //  {
        //    _xrefTable = xrefTable;
        //  }
        //
        //  public void Collect()
        //  { }
        //
        //  public PdfReference[] ReachableObjects()
        //  {
        //    Hash_table objects = new Hash_table();
        //    TransitiveClosure(objects, _xrefTable.document.trailer);
        //  }



        /// <summary>
        /// Gets the cross reference to an objects used for undefined indirect references.
        /// </summary>
        public PdfReference DeadObject
        {
            get
            {
                if (_deadObject == null)
                {
                    _deadObject = new PdfDictionary(_document);
                    Add(_deadObject);
                    _deadObject.Elements.Add("/DeadObjectCount", new PdfInteger());
                }
                return _deadObject.Reference;
            }
        }
        PdfDictionary _deadObject;

        internal void FixXRefs(bool forceDocument = false)
        {
            foreach (var item in AllReferences)
            {
                if (item.Value != null)
                {
                    FixUpObject(item.Value, forceDocument);
                }
                else
                {//what?!
                }
            }
        }


        internal void FixUpObject(PdfObject value, bool forceDocument)
        {

            PdfDictionary dict;
            PdfArray array;
            if ((dict = value as PdfDictionary) != null)
            {
                // Search for indirect references in all dictionary elements.
                PdfName[] names = dict.Elements.KeyNames;
                foreach (PdfName name in names)
                {
                    PdfItem item = dict.Elements[name];
                    Debug.Assert(item != null, "A dictionary element cannot be null.");

                    // Is item an iref?
                    PdfReference iref = item as PdfReference;
                    if (iref != null)
                    {
                        if (iref.Value == null || (forceDocument && ReferenceEquals(iref.Value, _document._irefTable[iref.ObjectID].Value) == false))
                        {
                            PdfObject irefValue = GetObject(iref.ObjectID, forceDocument);
                            if (irefValue.Reference == null)
                            {
                                iref.Value = irefValue;
                            }
                            else
                            {
                                dict.Elements[name] = irefValue.Reference;
                            }
                        }
                    }
                    else
                    {
                        // Case: The item is not a reference.
                        // If item is an object recursively fix its inner items.
                        PdfObject pdfObject = item as PdfObject;
                        if (pdfObject != null)
                        {
                            // Fix up inner objects, i.e. recursively walk down the object tree.
                            FixUpObject(pdfObject, forceDocument);
                        }
                    }
                }
            }
            else if ((array = value as PdfArray) != null)
            {
                // Search for indirect references in all array elements.
                int count = array.Elements.Count;
                for (int idx = 0; idx < count; idx++)
                {
                    PdfItem item = array.Elements[idx];
                    Debug.Assert(item != null, "An array element cannot be null.");

                    // Is item an iref?
                    PdfReference iref = item as PdfReference;
                    if (iref != null)
                    {
                        if (iref.Value == null || (forceDocument && ReferenceEquals(iref.Value, _document._irefTable[iref.ObjectID].Value) == false))
                        {
                            PdfObject irefValue = GetObject(iref.ObjectID, forceDocument);
                            if (irefValue.Reference == null)
                            {
                                iref.Value = irefValue;
                            }
                            else
                            {
                                array.Elements[idx] = irefValue.Reference;
                            }
                        }
                    }
                    else
                    {
                        // Case: The item is not a reference.
                        // If item is an object recursively fix its inner items.
                        PdfObject pdfObject = item as PdfObject;
                        if (pdfObject != null)
                        {
                            // Fix up inner objects, i.e. recursively walk down the object tree.
                            FixUpObject(pdfObject, forceDocument);
                        }
                    }
                }
            }
            else
            {
                // Case: The item is some other indirect object.
                // Indirect integers, booleans, etc. are allowed, but PDFsharp do not create them.
                // If such objects occur in imported PDF files from other producers, nothing more is to do.
                // The owner was already set, which is double checked by the assertions below.
                if (value is PdfNameObject || value is PdfStringObject || value is PdfBooleanObject || value is PdfIntegerObject || value is PdfNumberObject)
                {
                    Debug.Assert(value.IsIndirect);
                }
                else
                    Debug.Assert(false, "Should not come here. Object is neither a dictionary nor an array.");
            }
        }


        private PdfObject GetObject(PdfObjectID objectID, bool forceDocument)
        {
            PdfReference objRef = null;

            if (forceDocument == false)
            {
                objRef = this[objectID];

                if (objRef != null)
                {
                    return objRef.Value;
                }
            }

            objRef = _document._irefTable[objectID];
            if (objRef != null)
            {
                return objRef.Value;
            }

            return null;
        }
    }
}