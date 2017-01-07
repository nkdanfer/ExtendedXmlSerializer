﻿// MIT License
// 
// Copyright (c) 2016 Wojciech Nagórski
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

using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Xml.Linq;
using ExtendedXmlSerialization.Core;
using ExtendedXmlSerialization.Processing;

namespace ExtendedXmlSerialization.Model
{
    public interface IDeserializer
    {
        object Deserialize(Stream stream);
    }

    public class Deserializer<T> : IDeserializer
    {
        private readonly IDeserializer _deserializer;

        public Deserializer() : this(new Deserializer(typeof(T))) {}

        public Deserializer(IDeserializer deserializer)
        {
            _deserializer = deserializer;
        }

        public T Deserialize(Stream stream) => (T) _deserializer.Deserialize(stream);

        object IDeserializer.Deserialize(Stream stream) => Deserialize(stream);
    }

    public class Deserializer : IDeserializer
    {
        private readonly Typed? _type;
        private readonly IReader _reader;

        public Deserializer() : this(RootReader.Default) {}

        public Deserializer(Typed type) : this(RootReader.Default, type) {}

        public Deserializer(IReader reader, Typed? type = null)
        {
            _reader = reader;
            _type = type;
        }

        public object Deserialize(Stream stream)
        {
            var text = new StreamReader(stream).ReadToEnd();
            var element = XDocument.Parse(text).Root;
            var result = _reader.Read(element, _type);
            return result;
        }
    }

    public interface IReader
    {
        object Read(XElement element, Typed? hint = null);
    }

    public interface IReader<out T> : IReader
    {
        new T Read(XElement element, Typed? hint = null);
    }

    class RootReader : DecoratedReader
    {
        public static RootReader Default { get; } = new RootReader();
        RootReader() : this(SelectingReader.Default) {}

        public RootReader(IReader reader) : base(reader) {}
    }

    sealed class EnumReader : ReaderBase
    {
        public static EnumReader Default { get; } = new EnumReader();
        EnumReader() {}

        public override object Read(XElement element, Typed? hint = null)
        {
            if (hint.HasValue)
            {
                return Enum.Parse(hint, element.Value);
            }
            throw new InvalidOperationException(
                $"An attempt was made to read element as an enumeration, but no type was specified.");
        }
    }

    class SelectingReader : IReader
    {
        public static SelectingReader Default { get; } = new SelectingReader();
        SelectingReader() : this(Selectors.Default.Get(Types.Default).Self) {}

        private readonly ITypes _types;
        private readonly Func<ISelector> _selector;

        public SelectingReader(Func<ISelector> selector) : this(Types.Default, selector) {}

        public SelectingReader(ITypes types, Func<ISelector> selector)
        {
            _types = types;
            _selector = selector;
        }

        public object Read(XElement element, Typed? hint = null)
        {
            var typed = hint ?? _types.Get(element);
            var converter = _selector().Get(typed);
            var result = converter.Read(element, typed);
            return result;
        }
    }

    public abstract class ReaderBase : IReader
    {
        public abstract object Read(XElement element, Typed? hint = null);
    }

    public abstract class ReaderBase<T> : IReader<T>
    {
        object IReader.Read(XElement element, Typed? hint) => Read(element, hint);

        public abstract T Read(XElement element, Typed? hint = null);
    }

    public class ValueReader<T> : ReaderBase<T>
    {
        private readonly Func<string, T> _deserialize;

        public ValueReader(Func<string, T> deserialize)
        {
            _deserialize = deserialize;
        }

        public override T Read(XElement element, Typed? hint = null) => _deserialize(element.Value);
    }

    public class ValueValidatingReader : DecoratedReader
    {
        public ValueValidatingReader(IReader reader) : base(reader) {}
        public override object Read(XElement element, Typed? hint = null) => 
            element.Value.NullIfEmpty() != null ? base.Read(element, hint) : null;
    }

    public class DecoratedReader : ReaderBase
    {
        private readonly IReader _reader;

        public DecoratedReader(IReader reader)
        {
            _reader = reader;
        }

        public override object Read(XElement element, Typed? hint = null) => _reader.Read(element, hint);
    }

    public class ArrayReader : ListReaderBase
    {
        public ArrayReader(ITypes types, IReader reader) : base(types, reader) {}

        protected override object Create(Type listType, IEnumerable enumerable, Type elementType)
        {
            var list = new ArrayList();
            foreach (var item in enumerable)
            {
                list.Add(item);
            }

            var result = list.ToArray(elementType);
            return result;
        }
    }

    public class ListReader : ListReaderBase
    {
        private readonly IActivators _activators;
        private readonly IAddDelegates _add;

        public ListReader(ITypes types, IReader reader)
            : this(types, reader, Activators.Default, AddDelegates.Default) {}

        public ListReader(ITypes types, IReader reader, IActivators activators, IAddDelegates add)
            : base(types, reader)
        {
            _activators = activators;
            _add = add;
        }

        protected override object Create(Type listType, IEnumerable enumerable, Type elementType)
        {
            var result = _activators.Activate<object>(listType);
            var list = result as IList ?? new ListAdapter(result, _add.Get(listType));
            foreach (var item in enumerable)
            {
                list.Add(item);
            }
            return result;
        }
    }

    public abstract class ListReaderBase : ReaderBase
    {
        private readonly ITypes _types;
        private readonly IEnumeratingReader _reader;
        private readonly IElementTypeLocator _locator;

        protected ListReaderBase(ITypes types, IReader reader)
            : this(types, new EnumeratingReader(types, reader), ElementTypeLocator.Default) {}

        protected ListReaderBase(ITypes types, IEnumeratingReader reader, IElementTypeLocator locator)
        {
            _types = types;
            _reader = reader;
            _locator = locator;
        }

        public sealed override object Read(XElement element, Typed? hint = null)
        {
            var type = hint ?? _types.Get(element);
            var elementType = _locator.Locate(type);
            var enumerable = _reader.Read(element, elementType);
            var result = Create(type, enumerable, elementType);
            return result;
        }

        protected abstract object Create(Type listType, IEnumerable enumerable, Type elementType);
    }

    public interface IEnumeratingReader : IReader<IEnumerable> {}

    public class EnumeratingReader : ReaderBase<IEnumerable>, IEnumeratingReader
    {
        private readonly ITypes _types;
        private readonly IReader _reader;

        public EnumeratingReader(ITypes types, IReader reader)
        {
            _types = types;
            _reader = reader;
        }

        public override IEnumerable Read(XElement element, Typed? hint = null)
        {
            var elementType = hint.GetValueOrDefault().Info;
            foreach (var child in element.Elements())
            {
                var itemType = _types.Get(child)?.GetTypeInfo() ?? elementType;
                var item = _reader.Read(child, itemType);
                yield return item;
            }
        }
    }
}