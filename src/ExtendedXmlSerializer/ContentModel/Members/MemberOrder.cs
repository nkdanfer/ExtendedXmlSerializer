// MIT License
//
// Copyright (c) 2016 Wojciech Nag�rski
//                    Michael DeMond
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System.Collections.Generic;
using System.Reflection;
using ExtendedXmlSerializer.Core;
using ExtendedXmlSerializer.Core.Sources;
using JetBrains.Annotations;

namespace ExtendedXmlSerializer.ContentModel.Members
{
	sealed class MemberOrder : StructureCacheBase<MemberInfo, int>, IMemberOrder
	{
		readonly static DefaultMemberOrder Source = DefaultMemberOrder.Default;

		readonly IDictionary<MemberInfo, int> _store;
		readonly IParameterizedSource<MemberInfo, int> _source;

		[UsedImplicitly]
		public MemberOrder(IDictionary<MemberInfo, int> store) : this(store, Source) {}

		public MemberOrder(IDictionary<MemberInfo, int> store, IParameterizedSource<MemberInfo, int> source)
		{
			_store = store;
			_source = source;
		}

		protected override int Create(MemberInfo parameter) => _store.GetStructure(parameter) ?? _source.Get(parameter);
	}
}