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
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using PdfSharper.Pdf.Advanced;
using PdfSharper.Pdf.Security;
using PdfSharper.Pdf.Internal;
using System.Linq;
using PdfSharper.Pdf.AcroForms;

namespace PdfSharper.Pdf.IO
{
    /// <summary>
    /// Encapsulates the arguments of the PdfPasswordProvider delegate.
    /// </summary>
    public class PdfPasswordProviderArgs
    {
        /// <summary>
        /// Sets the password to open the document with.
        /// </summary>
        public string Password;

        /// <summary>
        /// When set to true the PdfReader.Open function returns null indicating that no PdfDocument was created.
        /// </summary>
        public bool Abort;
    }

    /// <summary>
    /// A delegated used by the PdfReader.Open function to retrieve a password if the document is protected.
    /// </summary>
    public delegate void PdfPasswordProvider(PdfPasswordProviderArgs args);

    /// <summary>
    /// Represents the functionality for reading PDF documents.
    /// </summary>
    public static class PdfReader
    {
        /// <summary>
        /// Determines whether the file specified by its path is a PDF file by inspecting the first eight
        /// bytes of the data. If the file header has the form �%PDF-x.y� the function returns the version
        /// number as integer (e.g. 14 for PDF 1.4). If the file header is invalid or inaccessible
        /// for any reason, 0 is returned. The function never throws an exception. 
        /// </summary>
        public static int TestPdfFile(string path)
        {
#if !NETFX_CORE
            FileStream stream = null;
            try
            {
                int pageNumber;
                string realPath = Drawing.XPdfForm.ExtractPageNumber(path, out pageNumber);
                if (File.Exists(realPath)) // prevent unwanted exceptions during debugging
                {
                    stream = new FileStream(realPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    byte[] bytes = new byte[1024];
                    stream.Read(bytes, 0, 1024);
                    return GetPdfFileVersion(bytes);
                }
            }
            // ReSharper disable once EmptyGeneralCatchClause
            catch { }
            finally
            {
                try
                {
                    if (stream != null)
                    {
#if UWP
                        stream.Dispose();
#else
                        stream.Close();
#endif
                    }
                }
                // ReSharper disable once EmptyGeneralCatchClause
                catch
                {
                }
            }
#endif
            return 0;
        }

        /// <summary>
        /// Determines whether the specified stream is a PDF file by inspecting the first eight
        /// bytes of the data. If the data begins with �%PDF-x.y� the function returns the version
        /// number as integer (e.g. 14 for PDF 1.4). If the data is invalid or inaccessible
        /// for any reason, 0 is returned. The function never throws an exception. 
        /// </summary>
        public static int TestPdfFile(Stream stream)
        {
            long pos = -1;
            try
            {
                pos = stream.Position;
                byte[] bytes = new byte[1024];
                stream.Read(bytes, 0, 1024);
                return GetPdfFileVersion(bytes);
            }
            // ReSharper disable once EmptyGeneralCatchClause
            catch { }
            finally
            {
                try
                {
                    if (pos != -1)
                        stream.Position = pos;
                }
                // ReSharper disable once EmptyGeneralCatchClause
                catch { }
            }
            return 0;
        }

        /// <summary>
        /// Determines whether the specified data is a PDF file by inspecting the first eight
        /// bytes of the data. If the data begins with �%PDF-x.y� the function returns the version
        /// number as integer (e.g. 14 for PDF 1.4). If the data is invalid or inaccessible
        /// for any reason, 0 is returned. The function never throws an exception. 
        /// </summary>
        public static int TestPdfFile(byte[] data)
        {
            return GetPdfFileVersion(data);
        }

        /// <summary>
        /// Implements scanning the PDF file version.
        /// </summary>
        internal static int GetPdfFileVersion(byte[] bytes)
        {
            try
            {
                // Acrobat accepts headers like �%!PS-Adobe-N.n PDF-M.m�...
                string header = PdfEncoders.RawEncoding.GetString(bytes, 0, bytes.Length);  // Encoding.ASCII.GetString(bytes);
                if (header[0] == '%' || header.IndexOf("%PDF", StringComparison.Ordinal) >= 0)
                {
                    int ich = header.IndexOf("PDF-", StringComparison.Ordinal);
                    if (ich > 0 && header[ich + 5] == '.')
                    {
                        char major = header[ich + 4];
                        char minor = header[ich + 6];
                        if (major >= '1' && major < '2' && minor >= '0' && minor <= '9')
                            return (major - '0') * 10 + (minor - '0');
                    }
                }
            }
            // ReSharper disable once EmptyGeneralCatchClause
            catch { }
            return 0;
        }

        /// <summary>
        /// Opens an existing PDF document.
        /// </summary>
        public static PdfDocument Open(string path, PdfDocumentOpenMode openmode)
        {
            return Open(path, null, openmode, null);
        }

        /// <summary>
        /// Opens an existing PDF document.
        /// </summary>
        public static PdfDocument Open(string path, PdfDocumentOpenMode openmode, PdfPasswordProvider provider)
        {
            return Open(path, null, openmode, provider);
        }

        /// <summary>
        /// Opens an existing PDF document.
        /// </summary>
        public static PdfDocument Open(string path, string password, PdfDocumentOpenMode openmode)
        {
            return Open(path, password, openmode, null);
        }

        /// <summary>
        /// Opens an existing PDF document.
        /// </summary>
        public static PdfDocument Open(string path, string password, PdfDocumentOpenMode openmode, PdfPasswordProvider provider)
        {
#if !NETFX_CORE
            PdfDocument document;
            Stream stream = null;
            try
            {
                stream = new FileStream(path, FileMode.Open, FileAccess.Read);
                document = Open(stream, password, openmode, provider);
                if (document != null)
                {
                    document._fullPath = Path.GetFullPath(path);
                }
            }
            finally
            {
                if (stream != null)
#if !UWP
                    stream.Close();
#else
                    stream.Dispose();
#endif
            }
            return document;
#else
                    return null;
#endif
        }

        /// <summary>
        /// Opens an existing PDF document.
        /// </summary>
        public static PdfDocument Open(string path)
        {
            return Open(path, null, PdfDocumentOpenMode.Modify, null);
        }

        /// <summary>
        /// Opens an existing PDF document.
        /// </summary>
        public static PdfDocument Open(string path, string password)
        {
            return Open(path, password, PdfDocumentOpenMode.Modify, null);
        }

        /// <summary>
        /// Opens an existing PDF document.
        /// </summary>
        public static PdfDocument Open(Stream stream, PdfDocumentOpenMode openmode)
        {
            return Open(stream, null, openmode);
        }

        /// <summary>
        /// Opens an existing PDF document.
        /// </summary>
        public static PdfDocument Open(Stream stream, PdfDocumentOpenMode openmode, PdfPasswordProvider passwordProvider)
        {
            return Open(stream, null, openmode, passwordProvider);
        }
        /// <summary>
        /// Opens an existing PDF document.
        /// </summary>
        public static PdfDocument Open(Stream stream, string password, PdfDocumentOpenMode openmode)
        {
            return Open(stream, password, openmode, null);
        }

        /// <summary>
        /// Opens an existing PDF document.
        /// </summary>
        public static PdfDocument Open(Stream stream, string password, PdfDocumentOpenMode openmode, PdfPasswordProvider passwordProvider)
        {
            if (!stream.CanRead || !stream.CanSeek)
                throw new ArgumentException("Cannot read document from an undreadable or unseekable stream.");

            PdfDocument document;
            try
            {
                Lexer lexer = new Lexer(stream);
                document = new PdfDocument(lexer);
                document._state |= DocumentState.Imported;
                document._openMode = openmode;
                document._fileSize = stream.Length;

                // Get file version.
                byte[] header = new byte[1024];
                stream.Position = 0;
                stream.Read(header, 0, 1024);
                document._version = GetPdfFileVersion(header);
                if (document._version == 0)
                    throw new InvalidOperationException(PSSR.InvalidPdf);

                document._irefTable.IsUnderConstruction = true;
                Parser parser = new Parser(document);
                // Read all trailers or cross-reference streams, but no objects.
                document._trailer = parser.ReadTrailer();

                Debug.Assert(document._irefTable.IsUnderConstruction);


                // Is document encrypted?
                PdfReference xrefEncrypt = document._trailer.Elements[PdfTrailer.Keys.Encrypt] as PdfReference;
                if (xrefEncrypt != null)
                {
                    //xrefEncrypt.Value = parser.ReadObject(null, xrefEncrypt.ObjectID, false);
                    PdfObject encrypt = parser.ReadObject(null, xrefEncrypt.ObjectID, false, false);

                    encrypt.Reference = xrefEncrypt;
                    xrefEncrypt.Value = encrypt;
                    PdfStandardSecurityHandler securityHandler = document.SecurityHandler;
                    TryAgain:
                    PasswordValidity validity = securityHandler.ValidatePassword(password);
                    if (validity == PasswordValidity.Invalid)
                    {
                        if (passwordProvider != null)
                        {
                            PdfPasswordProviderArgs args = new PdfPasswordProviderArgs();
                            passwordProvider(args);
                            if (args.Abort)
                                return null;
                            password = args.Password;
                            goto TryAgain;
                        }
                        else
                        {
                            if (password == null)
                                throw new PdfReaderException(PSSR.PasswordRequired);
                            else
                                throw new PdfReaderException(PSSR.InvalidPassword);
                        }
                    }
                    else if (validity == PasswordValidity.UserPassword && openmode == PdfDocumentOpenMode.Modify)
                    {
                        if (passwordProvider != null)
                        {
                            PdfPasswordProviderArgs args = new PdfPasswordProviderArgs();
                            passwordProvider(args);
                            if (args.Abort)
                                return null;
                            password = args.Password;
                            goto TryAgain;
                        }
                        else
                            throw new PdfReaderException(PSSR.OwnerPasswordRequired);
                    }
                }
                else
                {
                    if (password != null)
                    {
                        // Password specified but document is not encrypted.
                        // ignore
                    }
                }

                foreach (var trailer in document._trailers)
                {
                    DecompressObjects(parser, trailer.XRefTable);
                }

                //only case where we want to read most recent first
                //most recent needs to be what goes in the document_ireftable is why we read this first
                foreach (var trailer in document._trailers)
                {
                    ReadObjects(document, parser, trailer);
                }

                document._irefTable.IsUnderConstruction = false;

                bool foundNonCrossRef = false;
                foreach (var trailer in document._trailers)
                {
                    trailer.FixXRefs();
                    foundNonCrossRef = !(trailer is PdfCrossReferenceStream);
                }

                document.Options.CompressContentStreams = !foundNonCrossRef;

                //point to the latest version for everything
                document._irefTable.FixXRefs(true);

                bool signaturePresent = document.Internals.GetAllObjects().OfType<PdfDictionary>().Any(pd => pd.Elements.GetString(PdfSignatureField.Keys.Type) == "/Sig");


                if (signaturePresent)
                {
                    foreach (var trailer in document._trailers)
                    {
                        trailer.IsReadOnly = true;
                    }
                }
                else if (!signaturePresent && document._trailers.Count == 1)
                {
                    document._trailer.XRefTable = document._irefTable;

                    PdfPages pages = document.Pages;
                    Debug.Assert(pages != null);

                    document._trailer.Prev = null;
                    document._trailer.Next = null;
                }
                else if (document._trailers.All(t => t is PdfCrossReferenceStream) && signaturePresent) //cannot flatten, leave it
                {
                    document._trailers.ForEach(t => t.IsReadOnly = true);
                }
                else if (document._trailers.All(t => t is PdfCrossReferenceStream) && document._trailers.Count > 2 && !signaturePresent && document.IsLinearized) //adobe applied an incremental update to a linear document, we can still flatten
                {
                    var incrementalTrailer = document._trailers.FirstOrDefault(t => t.Next == null);
                    document._trailers.Remove(incrementalTrailer);
                    incrementalTrailer.Prev.Next = null; //remove link to this trailer

                    foreach (var iref in incrementalTrailer.XRefTable.AllReferences)
                    {
                        if (iref.Value == incrementalTrailer)
                        {
                            continue;
                        }

                        bool found = false;
                        foreach (var t in document._trailers)
                        {
                            if (t.XRefTable.Contains(iref.ObjectID))
                            {
                                found = true;
                                t.XRefTable[iref.ObjectID].SetObject(iref.Value);
                            }
                        }

                        if (!found)
                        {
                            incrementalTrailer.Prev.AddReference(iref);
                        }
                    }

                    foreach (var os in incrementalTrailer.ObjectStreams)
                    {
                        document.Internals.RemoveObject(os);
                    }
                    document._irefTable.Remove(incrementalTrailer.Reference);
                    if (incrementalTrailer.Reference.ContainingStreamID.IsEmpty == false)
                    {
                        PdfObjectStream containingStream = document._irefTable[incrementalTrailer.Reference.ContainingStreamID].Value as PdfObjectStream;
                        if (containingStream != null)
                        {
                            containingStream.RemoveObject(incrementalTrailer.Reference);
                            if (containingStream.Number == 0)
                            {
                                document.Internals.RemoveObject(containingStream);
                            }
                        }

                    }
                }

                // Encrypt all objects.
                if (xrefEncrypt != null)
                {
                    document.SecurityHandler.EncryptDocument();
                }

                document._trailer.Finish();

#if DEBUG_
    // Some tests...
                PdfReference[] reachables = document.xrefTable.TransitiveClosure(document.trailer);
                reachables.GetType();
                reachables = document.xrefTable.AllXRefs;
                document.xrefTable.CheckConsistence();
#endif

                if ((document._openMode == PdfDocumentOpenMode.Modify || document._trailers.Count == 1) && !document._trailers.Any(t => t.IsReadOnly))
                {
                    // Create new or change existing document IDs.
                    if (document.Internals.SecondDocumentID == "")
                        document._trailer.CreateNewDocumentIDs();
                    else
                    {
                        byte[] agTemp = Guid.NewGuid().ToByteArray();
                        document.Internals.SecondDocumentID = PdfEncoders.RawEncoding.GetString(agTemp, 0, agTemp.Length);
                    }

                    // Change modification date
                    document.Info.ModificationDate = DateTime.Now;
                }

                if (signaturePresent) //original stream must be preserved for the signature
                {
                    using (MemoryStream msCopy = new MemoryStream((int)stream.Length))
                    {
                        stream.Seek(0, SeekOrigin.Begin);
                        stream.CopyTo(msCopy);
                        document.fileContents = msCopy.ToArray();
                    }
                }

                //everything is parsed, cleanup memory
                foreach (PdfTrailer trailer in document._trailers)
                {
                    foreach (var objStream in trailer.ObjectStreams)
                    {
                        objStream.Dispose();
                    }
                }

                document.UnderConstruction = false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
                throw;
            }
            return document;
        }

        private static void DecompressObjects(Parser parser, PdfCrossReferenceTable xRefTable)
        {
            PdfReference[] irefs2 = xRefTable.AllReferences;

            int count2 = irefs2.Length;

            // 1st: Create iRefs for all compressed objects.
            Dictionary<int, object> objectStreams = new Dictionary<int, object>();
            for (int idx = 0; idx < count2; idx++)
            {
                PdfReference iref = irefs2[idx];
                PdfCrossReferenceStream xrefStream = iref.Value as PdfCrossReferenceStream;
                if (xrefStream != null)
                {
                    for (int idx2 = 0; idx2 < xrefStream.Entries.Count; idx2++)
                    {
                        PdfCrossReferenceStream.CrossReferenceStreamEntry item = xrefStream.Entries[idx2];
                        // Is type xref to compressed object?
                        if (item.Type == 2)
                        {
                            //PdfReference irefNew = parser.ReadCompressedObject(new PdfObjectID((int)item.Field2), (int)item.Field3);
                            //document._irefTable.Add(irefNew);
                            int objectNumber = (int)item.Field2;
                            if (!objectStreams.ContainsKey(objectNumber))
                            {
                                objectStreams.Add(objectNumber, null);
                                PdfObjectID objectID = new PdfObjectID(objectNumber);
                                parser.ReadIRefsFromCompressedObject(objectID, xRefTable);
                            }
                        }
                    }
                }
            }

            // 2nd: Read compressed objects.
            for (int idx = 0; idx < count2; idx++)
            {
                PdfReference iref = irefs2[idx];
                PdfCrossReferenceStream xrefStream = iref.Value as PdfCrossReferenceStream;
                if (xrefStream != null)
                {
                    for (int idx2 = 0; idx2 < xrefStream.Entries.Count; idx2++)
                    {
                        PdfCrossReferenceStream.CrossReferenceStreamEntry item = xrefStream.Entries[idx2];
                        // Is type xref to compressed object?
                        if (item.Type == 2)
                        {
                            PdfReference irefNew = parser.ReadCompressedObject(new PdfObjectID((int)item.Field2),
                                (int)item.Field3, xRefTable);
                            Debug.Assert(xRefTable.Contains(irefNew.ObjectID));
                            //document._irefTable.Add(irefNew);
                        }
                    }
                }
            }
        }

        private static void ReadObjects(PdfDocument document, Parser parser, PdfTrailer trailer)
        {
            PdfCrossReferenceStream crossRefStream = trailer as PdfCrossReferenceStream;
            bool isCrossReferenceStream = crossRefStream != null;
            PdfReference[] irefs = trailer.XRefTable.AllReferences;
            int count = irefs.Length;

            PdfReference hintStreamReference = null;
            // Read all indirect objects.
            for (int idx = 0; idx < count; idx++)
            {
                PdfReference iref = irefs[idx];
                if (iref.Value == null)
                {
#if DEBUG_
                        if (iref.ObjectNumber == 1074)
                            iref.GetType();
#endif
                    if (isCrossReferenceStream && document._irefTable.Contains(iref.ObjectID) && document._irefTable[iref.ObjectID].Value != null)
                    {
                        trailer.XRefTable.Remove(iref);
                        trailer.XRefTable.Add(document._irefTable[iref.ObjectID]);
                        continue;
                    }
                    try
                    {
                        Debug.Assert(trailer.XRefTable.Contains(iref.ObjectID));
                        PdfObject pdfObject = parser.ReadObject(null, iref.ObjectID, false, false, false, trailer.XRefTable);

                        iref.Value = pdfObject;

                        if (pdfObject is PdfDictionary)
                        {
                            PdfDictionary objDictionary = pdfObject as PdfDictionary;
                            if (document.LinearizationParamaters == null)
                            {
                                if (objDictionary.Elements.ContainsKey(PdfLinearizationParameters.Keys.Linearized))
                                {
                                    document.LinearizationParamaters = new PdfLinearizationParameters(objDictionary);
                                    int hintStreamPosition = document.LinearizationParamaters.Elements.GetArray(PdfLinearizationParameters.Keys.Hint).Elements.GetInteger(0);
                                    hintStreamReference = trailer.XRefTable.AllReferences.SingleOrDefault(href => href.Position == hintStreamPosition);
                                }
                            }

                            if (objDictionary.Elements.GetName(PdfFormXObject.Keys.Type) == "/XObject")
                            {
                                iref.Value = new PdfFormXObject(objDictionary);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(ex.Message);
                        // 4STLA rethrow exception to notify caller.
                        throw;
                    }
                }
                else
                {
                    Debug.Assert(trailer.XRefTable.Contains(iref.ObjectID));
                    //iref.GetType();
                }

                if (iref == hintStreamReference && document.LinearizationParamaters != null)
                {
                    document.LinearizationParamaters.HintStream = (PdfDictionary)iref.Value;
                }

                if (iref.Value is PdfObjectStream)
                {
                    trailer.ObjectStreams.Add((PdfObjectStream)iref.Value);
                }
            }



        }

        /// <summary>
        /// Opens an existing PDF document.
        /// </summary>
        public static PdfDocument Open(Stream stream)
        {
            return Open(stream, PdfDocumentOpenMode.Modify);
        }
    }
}
