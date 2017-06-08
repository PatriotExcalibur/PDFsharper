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

using PdfSharper.Drawing;
using PdfSharper.Pdf.Advanced;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace PdfSharper.Pdf.AcroForms
{
    /// <summary>
    /// Represents a interactive form (or AcroForm), a collection of fields for 
    /// gathering information interactively from the user.
    /// </summary>
    public sealed class PdfAcroForm : PdfDictionary
    {
        /// <summary>
        /// Initializes a new instance of AcroForm.
        /// </summary>
        internal PdfAcroForm(PdfDocument document)
            : base(document)
        {
            _document = document;
        }

        internal PdfAcroForm(PdfDictionary dictionary)
            : base(dictionary)
        { }

        /// <summary>
        /// Gets the fields collection of this form.
        /// </summary>
        public PdfAcroField.PdfAcroFieldCollection Fields
        {
            get
            {
                if (_fields == null)
                {
                    object o = Elements.GetValue(Keys.Fields, VCF.CreateIndirect);
                    _fields = (PdfAcroField.PdfAcroFieldCollection)o;
                }
                return _fields;
            }
        }
        PdfAcroField.PdfAcroFieldCollection _fields;

        /// <summary>
        /// Flattens the AcroForm by rendering Field-contents directly onto the page.
        /// </summary>
        public void Flatten()
        {
            Queue<PdfAcroField> fieldsToRemove = new Queue<PdfAcroField>(Fields);
            while (fieldsToRemove.Count > 0)
            {
                var field = fieldsToRemove.Dequeue();
                field.Flatten();
            }

            RemoveJavascript();
        }

        public void RemoveJavascript()
        {
            string namesKey = "/Names";
            string javascriptKey = "/JavaScript";

            if (_document != null && _document.Catalog != null && _document.Catalog.Elements != null && _document.Catalog.Elements.Any(e => e.Key == namesKey))
            {
                PdfDictionary names = _document.Catalog.Elements.GetDictionary(namesKey);
                if (names != null && names.Elements != null && names.Elements.Any(e => e.Key == javascriptKey))
                {
                    names.Elements.Remove(javascriptKey);
                }
            }
        }

        internal override void PrepareForSave()
        {
            base.PrepareForSave();

            IEnumerable<PdfAcroField> typedFields = Fields;

            var allFields = typedFields.Concat(typedFields.SelectMany(tf => WalkAllFields(tf)));
            foreach (var element in allFields)
            {
                element.PrepareForSave();
            }
        }

        public IEnumerable<PdfAcroField> WalkAllFields(PdfAcroField current)
        {
            if (current != null)
            {
                if (!current.HasKids)
                {
                    yield return current;
                    yield break;
                }

                foreach (var child in current.Fields)
                {
                    var subchildren = WalkAllFields(child);
                    foreach (var subChild in subchildren)
                    {
                        yield return subChild;
                    }
                }
            }
        }

        private XFont _defaultFont;

        internal XFont GetDefaultFont()
        {
            lock (this)
            {
                if (_defaultFont == null)
                {
                    var defaultFormResources = Elements.GetDictionary(PdfAcroForm.Keys.DR);
                    if (defaultFormResources != null && defaultFormResources.Elements.ContainsKey(PdfResources.Keys.Font))
                    {
                        var fontResourceItem = XForm.GetFontResourceItem("Arial", defaultFormResources);
                        if (string.IsNullOrEmpty(fontResourceItem.Key))
                        {
                            fontResourceItem = XForm.GetFontResourceItem("Helvetica", defaultFormResources);
                            _defaultFont = new XFont("Helvetica", 10);
                        }
                        else
                            _defaultFont = new XFont("Arial", 10);

                        Debug.Assert(!string.IsNullOrEmpty(fontResourceItem.Key), "Unable to find a default font");

                    }
                }
            }

            return _defaultFont;
        }

        /// <summary>
        /// Predefined keys of this dictionary. 
        /// The description comes from PDF 1.4 Reference.
        /// </summary>
        public sealed class Keys : KeysBase
        {
            // ReSharper disable InconsistentNaming

            /// <summary>
            /// (Required) An array of references to the document�s root fields (those with
            /// no ancestors in the field hierarchy).
            /// </summary>
            [KeyInfo(KeyType.Array | KeyType.Required, typeof(PdfAcroField.PdfAcroFieldCollection))]
            public const string Fields = "/Fields";

            /// <summary>
            /// (Optional) A flag specifying whether to construct appearance streams and
            /// appearance dictionaries for all widget annotations in the document.
            /// Default value: false.
            /// </summary>
            [KeyInfo(KeyType.Boolean | KeyType.Optional)]
            public const string NeedAppearances = "/NeedAppearances";

            /// <summary>
            /// (Optional; PDF 1.3) A set of flags specifying various document-level characteristics
            /// related to signature fields.
            /// Default value: 0.
            /// </summary>
            [KeyInfo("1.3", KeyType.Integer | KeyType.Optional)]
            public const string SigFlags = "/SigFlags";

            /// <summary>
            /// (Required if any fields in the document have additional-actions dictionaries
            /// containing a C entry; PDF 1.3) An array of indirect references to field dictionaries
            /// with calculation actions, defining the calculation order in which their values will 
            /// be recalculated when the value of any field changes.
            /// </summary>
            [KeyInfo(KeyType.Array)]
            public const string CO = "/CO";

            /// <summary>
            /// (Optional) A document-wide default value for the DR attribute of variable text fields.
            /// </summary>
            [KeyInfo(KeyType.Dictionary | KeyType.Optional)]
            public const string DR = "/DR";

            /// <summary>
            /// (Optional) A document-wide default value for the DA attribute of variable text fields.
            /// </summary>
            [KeyInfo(KeyType.String | KeyType.Optional)]
            public const string DA = "/DA";

            /// <summary>
            /// (Optional) A document-wide default value for the Q attribute of variable text fields.
            /// </summary>
            [KeyInfo(KeyType.Integer | KeyType.Optional)]
            public const string Q = "/Q";

            /// <summary>
            /// Gets the KeysMeta for these keys.
            /// </summary>
            internal static DictionaryMeta Meta
            {
                get
                {
                    if (s_meta == null)
                        s_meta = CreateMeta(typeof(Keys));
                    return s_meta;
                }
            }
            static DictionaryMeta s_meta;

            // ReSharper restore InconsistentNaming
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
