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

using System.Collections;
using ExtendedXmlSerializer.ContentModel.Xml;

namespace ExtendedXmlSerializer.ContentModel.Collections
{
	sealed class CollectionContentsReader : DecoratedReader
	{
		readonly static Lists Lists = Lists.Default;

		readonly ICollectionItemReader _item;
		readonly ILists _lists;

		public CollectionContentsReader(IReader reader, IReader item) : this(reader, new CollectionItemReader(item)) {}

		public CollectionContentsReader(IReader reader, ICollectionItemReader item) : this(reader, item, Lists) {}

		public CollectionContentsReader(IReader reader, ICollectionItemReader item, ILists lists) : base(reader)
		{
			_item = item;
			_lists = lists;
		}

		public override object Get(IXmlReader parameter)
		{
			var result = base.Get(parameter);
			var token = parameter.New();
			if (token.HasValue)
			{
				var list = result as IList ?? _lists.Get(result);
				var t = token.Value;
				while (parameter.Next(t))
				{
					_item.Read(parameter, result, list);
				}
			}
			return result;
		}
	}
}