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
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;
using ExtendedXmlSerialization.Core;
using ExtendedXmlSerialization.Core.Sources;
using ExtendedXmlSerialization.Core.Specifications;
using ExtendedXmlSerialization.Processing;

namespace ExtendedXmlSerialization.Model
{
    public interface IMember : IReader, IWriter
    {
        XName Name { get; }
    }

    public abstract class MemberBase : IMember
    {
        private readonly IReader _reader;
        private readonly IWriter _writer;
        private readonly Typed _memberType;
        private readonly Func<object, object> _getter;

        protected MemberBase(IReader reader, IWriter writer, XName name, Typed memberType, Func<object, object> getter)
        {
            Name = name;
            _reader = reader;
            _writer = writer;
            _memberType = memberType;
            _getter = getter;
        }

        public XName Name { get; }

        public void Write(XmlWriter writer, object instance) => _writer.Write(writer, Get(instance));

        protected virtual object Get(object instance) => _getter(instance);

        public object Read(XElement element, Typed? hint = null) => _reader.Read(element, hint ?? _memberType);
    }

    public interface IAssignableMember : IMember
    {
        void Set(object instance, object value);
    }

    public class ReadOnlyCollectionMember : MemberBase, IAssignableMember
    {
        private readonly Action<object, object> _add;

        public ReadOnlyCollectionMember(IReader reader, IWriter writer, XName name, Typed memberType,
                                        Func<object, object> getter, Action<object, object> add)
            : base(reader, writer, name, memberType, getter)
        {
            _add = add;
        }

        public void Set(object instance, object value)
        {
            var target = Get(instance);
            foreach (var element in value.AsValid<IEnumerable>())
            {
                _add(target, element);
            }
        }
    }

    public class AssignableMember : MemberBase, IAssignableMember
    {
        private readonly Action<object, object> _setter;

        public AssignableMember(IReader reader, IWriter writer, XName name, Typed memberType,
                                Func<object, object> getter,
                                Action<object, object> setter) : base(reader, writer, name, memberType, getter)
        {
            _setter = setter;
        }

        public void Set(object instance, object value) => _setter(instance, value);
    }


    public interface IMembers : IParameterizedSource<XName, IMember>, IEnumerable<IMember> {}

    public interface IInstanceMembers : IParameterizedSource<TypeInfo, IMembers> {}

    class InstanceMembers : WeakCacheBase<TypeInfo, IMembers>, IInstanceMembers
    {
        private readonly IMemberFactory _factory;

        public InstanceMembers(IMemberFactory factory)
        {
            _factory = factory;
        }

        protected override IMembers Create(TypeInfo parameter) =>
            new Members(CreateMembers(new Typed(parameter)).OrderBy(x => x.Sort).Select(x => x.Member));

        IEnumerable<MemberSort> CreateMembers(Typed declaringType)
        {
            foreach (var property in declaringType.Info.GetProperties())
            {
                var getMethod = property.GetGetMethod(true);
                if (property.CanRead && !getMethod.IsStatic && getMethod.IsPublic &&
                    !(!property.GetSetMethod(true)?.IsPublic ?? false) &&
                    property.GetIndexParameters().Length <= 0 &&
                    !property.IsDefined(typeof(XmlIgnoreAttribute), false))
                {
                    var type = new Typed(property.PropertyType.AccountForNullable());
                    var member = Create(property, type, property.CanWrite);
                    if (member != null)
                    {
                        yield return member.Value;
                    }
                }
            }

            foreach (var field in declaringType.Info.GetFields())
            {
                var readOnly = field.IsInitOnly;
                if ((readOnly ? !field.IsLiteral : !field.IsStatic) &&
                    !field.IsDefined(typeof(XmlIgnoreAttribute), false))
                {
                    var type = new Typed(field.FieldType.AccountForNullable());
                    var member = Create(field, type, !readOnly);
                    if (member != null)
                    {
                        yield return member.Value;
                    }
                }
            }
        }

        private MemberSort? Create(MemberInfo metadata, Typed memberType, bool assignable)
        {
            var member = _factory.Create(metadata, memberType, assignable);
            if (member != null)
            {
                var sort = new Sort(metadata.GetCustomAttribute<XmlElementAttribute>(false)?.Order,
                                    metadata.MetadataToken);

                var result = new MemberSort(member, sort);
                return result;
            }

            // TODO: Warning? Throw?
            return null;
        }

        struct MemberSort
        {
            public MemberSort(IMember member, Sort sort)
            {
                Member = member;
                Sort = sort;
            }

            public IMember Member { get; }
            public Sort Sort { get; }
        }
    }

    public interface IMemberFactory
    {
        IMember Create(MemberInfo metadata, Typed memberType, bool assignable);
    }

    class LegacySetterFactory : ISetterFactory
    {
        private readonly ISerializationToolsFactory _tools;
        private readonly ISetterFactory _factory;

        public LegacySetterFactory(ISerializationToolsFactory tools, ISetterFactory factory)
        {
            _tools = tools;
            _factory = factory;
        }

        public Action<object, object> Get(MemberInfo parameter)
        {
            var result = _factory.Get(parameter);
            var configuration = _tools.GetConfiguration(parameter.DeclaringType);
            if (configuration != null)
            {
                if (configuration.CheckPropertyEncryption(parameter.Name))
                {
                    var algorithm = _tools.EncryptionAlgorithm;
                    if (algorithm != null)
                    {
                        return new Decrypt(algorithm, result).Assign;
                    }
                }
            }
            return result;
        }

        sealed class Decrypt
        {
            private readonly IPropertyEncryption _encryption;
            private readonly Action<object, object> _source;
            public Decrypt(IPropertyEncryption encryption, Action<object, object> source)
            {
                _encryption = encryption;
                _source = source;
            }

            public void Assign(object instance, object value)
            {
                var text = _encryption.Decrypt(value as string ?? value.ToString());
                _source(instance, text);
            }
        }
    }

    class LegacyGetterFactory : IGetterFactory
    {
        private readonly ISerializationToolsFactory _tools;
        private readonly IGetterFactory _factory;

        public LegacyGetterFactory(ISerializationToolsFactory tools, IGetterFactory factory)
        {
            _tools = tools;
            _factory = factory;
        }

        public Func<object, object> Get(MemberInfo parameter)
        {
            var result = _factory.Get(parameter);
            var configuration = _tools.GetConfiguration(parameter.DeclaringType);
            if (configuration != null)
            {
                if (configuration.CheckPropertyEncryption(parameter.Name))
                {
                    var algorithm = _tools.EncryptionAlgorithm;
                    if (algorithm != null)
                    {
                        return new Encrypt(algorithm, result).Get;
                    }
                }
            }
            return result;
        }

        sealed class Encrypt : IParameterizedSource<object, object>
        {
            private readonly IPropertyEncryption _encryption;
            private readonly Func<object, object> _source;
            public Encrypt(IPropertyEncryption encryption, Func<object, object> source)
            {
                _encryption = encryption;
                _source = source;
            }

            public object Get(object parameter)
            {
                var value = _source(parameter);
                var result = _encryption.Encrypt(value as string ?? value.ToString());
                return result;
            }
        }
    }

    class LegacyMemberFactory : IMemberFactory
    {
        private readonly ISerializationToolsFactory _tools;
        private readonly IMemberFactory _factory;

        public LegacyMemberFactory(ISerializationToolsFactory tools, IMemberFactory factory)
        {
            _tools = tools;
            _factory = factory;
        }

        public IMember Create(MemberInfo metadata, Typed memberType, bool assignable)
        {
            var result = _factory.Create(metadata, memberType, assignable);
            var assignableMember = result as IAssignableMember;
            if (assignableMember != null)
            {
                var configuration = _tools.GetConfiguration(metadata.DeclaringType);
                if (configuration != null)
                {
                    if (configuration.CheckPropertyEncryption(metadata.Name))
                    {
                        var algorithm = _tools.EncryptionAlgorithm;
                        if (algorithm != null)
                        {
                            return new EncryptedMember(algorithm, assignableMember);
                            // value = algorithm.Decrypt(value);
                        }
                    }
                }
            }
            return result;
        }
    }

    public class EncryptedMember : IAssignableMember
    {
        private readonly IPropertyEncryption _encryption;
        private readonly IAssignableMember _member;

        public EncryptedMember(IPropertyEncryption encryption, IAssignableMember member)
        {
            _encryption = encryption;
            _member = member;
        }

        public object Read(XElement element, Typed? hint = null)
        {
            element.Value = _encryption.Decrypt(element.Value);
            return _member.Read(element, hint);
        }

        public void Write(XmlWriter writer, object instance)
        {
            _member.Write(writer, instance);
        }

        public XName Name => _member.Name;

        public void Set(object instance, object value)
        {
            _member.Set(instance, value);
        }
    }

    public class MemberFactory : IMemberFactory
    {
        private readonly IEnumeratingReader _reader;
        private readonly IConverter _converter;
        private readonly INames _names;
        private readonly INameProvider _name;
        private readonly IGetterFactory _getter;
        private readonly ISetterFactory _setter;
        private readonly IAddDelegates _add;

        public MemberFactory(IConverter converter, IEnumeratingReader reader, IGetterFactory getter)
            : this(converter, reader, AllNames.Default, MemberNameProvider.Default, getter, 
                   SetterFactory.Default, AddDelegates.Default) {}

        public MemberFactory(IConverter converter, IEnumeratingReader reader, INames names, INameProvider name,
                             IGetterFactory getter, ISetterFactory setter, IAddDelegates add)
        {
            _converter = converter;
            _reader = reader;
            _names = names;
            _name = name;
            _getter = getter;
            _setter = setter;
            _add = add;
        }

        public IMember Create(MemberInfo metadata, Typed memberType, bool assignable)
        {
            var name = XName.Get(_name.Get(metadata).LocalName,
                                 _names.Get(metadata.DeclaringType.GetTypeInfo()).NamespaceName);
            var getter = _getter.Get(metadata);

            if (assignable)
            {
                var type = new TypeEmittingWriter(new EmitTypeSpecification(memberType), _converter);
                var writer = new InstanceValidatingWriter(new ElementWriter(name.Accept, type));
                var result = new AssignableMember(_converter, writer, name, memberType, getter, _setter.Get(metadata));
                return result;
            }

            var add = _add.Get(memberType);
            if (add != null)
            {
                var writer = new ElementWriter(name.Accept, _converter);
                var result = new ReadOnlyCollectionMember(_reader, writer, name, memberType, getter, add);
                return result;
            }
            return null;
        }
    }

    sealed class Members : IMembers
    {
        private readonly ImmutableArray<IMember> _items;
        private readonly IDictionary<XName, IMember> _lookup;

        public Members(IEnumerable<IMember> items) : this(items.ToImmutableArray()) {}
        public Members(ImmutableArray<IMember> items) : this(items, items.ToDictionary(x => x.Name)) {}

        public Members(ImmutableArray<IMember> items, IDictionary<XName, IMember> lookup)
        {
            _items = items;
            _lookup = lookup;
        }

        public IMember Get(XName parameter)
        {
            IMember result;
            return _lookup.TryGetValue(parameter, out result) ? result : null;
        }

        public IEnumerator<IMember> GetEnumerator() => ((IEnumerable<IMember>) _items).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public interface ISetterFactory : IParameterizedSource<MemberInfo, Action<object, object>> {}

    class SetterFactory : ISetterFactory
    {
        public static SetterFactory Default { get; } = new SetterFactory();
        SetterFactory() {}

        public Action<object, object> Get(MemberInfo parameter) => Get(parameter.DeclaringType, parameter.Name);

        static Action<object, object> Get(Type type, string name)
        {
            // Object (type object) from witch the data are retrieved
            var itemObject = Expression.Parameter(typeof(object), "item");

            // Object casted to specific type using the operator "as".
            var itemCasted = type.GetTypeInfo().IsValueType
                ? Expression.Unbox(itemObject, type)
                : Expression.Convert(itemObject, type);
            // Property from casted object
            var property = Expression.PropertyOrField(itemCasted, name);

            // Secound parameter - value to set
            var value = Expression.Parameter(typeof(object), "value");

            // Because we use this function also for value type we need to add conversion to object
            var paramCasted = Expression.Convert(value, property.Type);

            // Assign value to property
            var assign = Expression.Assign(property, paramCasted);

            var lambda = Expression.Lambda<Action<object, object>>(assign, itemObject, value);

            var result = lambda.Compile();
            return result;
        }
    }


    public interface IGetterFactory : IParameterizedSource<MemberInfo, Func<object, object>> {}

    public class GetterFactory : IGetterFactory
    {
        public static GetterFactory Default { get; } = new GetterFactory();
        GetterFactory() {}

        public Func<object, object> Get(MemberInfo parameter) => Get(parameter.DeclaringType, parameter.Name);

        static Func<object, object> Get(Type type, string name)
        {
            // Object (type object) from witch the data are retrieved
            var itemObject = Expression.Parameter(typeof(object), "item");

            // Object casted to specific type using the operator "as".
            var itemCasted = Expression.Convert(itemObject, type);

            // Property from casted object
            var property = Expression.PropertyOrField(itemCasted, name);

            // Because we use this function also for value type we need to add conversion to object
            var conversion = Expression.Convert(property, typeof(object));
            var lambda = Expression.Lambda<Func<object, object>>(conversion, itemObject);
            var result = lambda.Compile();
            return result;
        }
    }

    struct Sort : IComparable<Sort>
    {
        public Sort(int? assigned, int value)
        {
            Assigned = assigned;
            Value = value;
        }

        int? Assigned { get; }
        int Value { get; }

        public int CompareTo(Sort other)
        {
            var right = !other.Assigned.HasValue;
            if (!Assigned.HasValue && right)
            {
                return Value.CompareTo(other.Value);
            }
            var compare = Assigned.GetValueOrDefault(-1).CompareTo(other.Assigned.GetValueOrDefault(-1));
            var result = right ? -compare : compare;
            return result;
        }
    }
}