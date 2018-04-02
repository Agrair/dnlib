﻿// dnlib: See LICENSE.txt for more info

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using dnlib.DotNet.Emit;
using dnlib.DotNet.Pdb.Symbols;
using dnlib.DotNet.Pdb.WindowsPdb;

namespace dnlib.DotNet.Pdb.Dss {
	sealed class SymbolReaderImpl : SymbolReader {
		ModuleDef module;
		readonly ISymUnmanagedReader reader;
		readonly object[] objsToKeepAlive;

		const int E_FAIL = unchecked((int)0x80004005);

		public SymbolReaderImpl(ISymUnmanagedReader reader, object[] objsToKeepAlive) {
			this.reader = reader ?? throw new ArgumentNullException(nameof(reader));
			this.objsToKeepAlive = objsToKeepAlive ?? throw new ArgumentNullException(nameof(objsToKeepAlive));
		}

		~SymbolReaderImpl() => Dispose(false);

		public override PdbFileKind PdbFileKind => PdbFileKind.WindowsPDB;

		public override int UserEntryPoint {
			get {
				int hr = reader.GetUserEntryPoint(out uint token);
				if (hr == E_FAIL)
					token = 0;
				else
					Marshal.ThrowExceptionForHR(hr);
				return (int)token;
			}
		}

		public override IList<SymbolDocument> Documents {
			get {
				if (documents == null) {
					reader.GetDocuments(0, out uint numDocs, null);
					var unDocs = new ISymUnmanagedDocument[numDocs];
					reader.GetDocuments((uint)unDocs.Length, out numDocs, unDocs);
					var docs = new SymbolDocument[numDocs];
					for (uint i = 0; i < numDocs; i++)
						docs[i] = new SymbolDocumentImpl(unDocs[i]);
					documents = docs;
				}
				return documents;
			}
		}
		volatile SymbolDocument[] documents;

		public override void Initialize(ModuleDef module) => this.module = module;

		public override SymbolMethod GetMethod(MethodDef method, int version) {
			int hr = reader.GetMethodByVersion(method.MDToken.Raw, version, out var unMethod);
			if (hr == E_FAIL)
				return null;
			Marshal.ThrowExceptionForHR(hr);
			return unMethod == null ? null : new SymbolMethodImpl(this, unMethod);
		}

		internal void GetCustomDebugInfos(SymbolMethodImpl symMethod, MethodDef method, CilBody body, IList<PdbCustomDebugInfo> result) {
			var asyncMethod = PseudoCustomDebugInfoFactory.TryCreateAsyncMethod(method.Module, method, body, symMethod.AsyncKickoffMethod, symMethod.AsyncStepInfos, symMethod.AsyncCatchHandlerILOffset);
			if (asyncMethod != null)
				result.Add(asyncMethod);

			const string CDI_NAME = "MD2";
			reader.GetSymAttribute(method.MDToken.Raw, CDI_NAME, 0, out uint bufSize, null);
			if (bufSize == 0)
				return;
			var cdiData = new byte[bufSize];
			reader.GetSymAttribute(method.MDToken.Raw, CDI_NAME, (uint)cdiData.Length, out bufSize, cdiData);
			PdbCustomDebugInfoReader.Read(method, body, result, cdiData);
		}

		public override void GetCustomDebugInfos(int token, GenericParamContext gpContext, IList<PdbCustomDebugInfo> result) {
		}

		public override void Dispose() {
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		void Dispose(bool disposing) {
			(reader as ISymUnmanagedDispose)?.Destroy();
			foreach (var obj in objsToKeepAlive)
				(obj as IDisposable)?.Dispose();
		}

		public bool CheckVersion(Guid pdbId, uint stamp, uint age) {
			if (reader is ISymUnmanagedReader4 reader4) {
				// Only id and age are verified
				int hr = reader4.MatchesModule(pdbId, stamp, age, out bool result);
				if (hr < 0)
					return false;
				return result;
			}

			// There seems to be no other method that can verify that we opened the correct PDB, so return true
			return true;
		}
	}
}
