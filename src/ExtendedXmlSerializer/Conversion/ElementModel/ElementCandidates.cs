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

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ExtendedXmlSerialization.Conversion.Legacy;
using ExtendedXmlSerialization.Core;
using ExtendedXmlSerialization.Core.Sources;
using ExtendedXmlSerialization.Core.Specifications;

namespace ExtendedXmlSerialization.Conversion.ElementModel
{
    public class ElementCandidates : IEnumerable<ICandidate<TypeInfo, IElement>>
    {
        private readonly IDictionary<TypeInfo, IElement> _elements;
        public ElementCandidates() : this(Defaults.Elements) {}

        public ElementCandidates(IEnumerable<IElement> elements)
            : this(elements.ToDictionary(x => x.ReferencedType)) {}

        public ElementCandidates(IDictionary<TypeInfo, IElement> elements)
        {
            _elements = elements;
        }

        public IEnumerator<ICandidate<TypeInfo, IElement>> GetEnumerator()
        {
            yield return new ElementCandidate(new DelegatedSpecification<TypeInfo>(_elements.ContainsKey),
                                              _elements.TryGet);
            yield return new ElementCandidate(IsAssignableSpecification<Enum>.Default, x => new Element(x));
            yield return new ElementCandidate(Specification.Instance, LegacyEnumerablePropertyProvider.Default.Get);
            yield return new ElementCandidate(ElementProvider.Default.Get);
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        sealed class Specification : ISpecification<TypeInfo>
        {
            readonly private static TypeInfo TypeInfo = typeof(IEnumerable).GetTypeInfo();

            public static Specification Instance { get; } = new Specification();
            Specification() {}

            public bool IsSatisfiedBy(TypeInfo parameter) =>
                parameter.IsArray || parameter.IsGenericType && TypeInfo.IsAssignableFrom(parameter);
        }
    }
}