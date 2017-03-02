﻿using System.IO;
using ExtendedXmlSerialization.Configuration;

namespace ExtendedXmlSerialization.Test.Support
{
	class SerializationSupport : IExtendedXmlSerializerTestSupport
	{
		public static SerializationSupport Default { get; } = new SerializationSupport();
		SerializationSupport() : this(new ExtendedXmlConfiguration().Create()) {}

		readonly IExtendedXmlSerializer _serializer;

		public SerializationSupport(IExtendedXmlSerializer serializer)
		{
			_serializer = serializer;
		}

		public T Assert<T>(T instance, string expected)
		{
			var data = _serializer.Serialize(instance);
			Xunit.Assert.Equal(expected, data);
			var result = _serializer.Deserialize<T>(data);
			return result;
		}

		public void Serialize(Stream stream, object instance) => _serializer.Serialize(stream, instance);

		public object Deserialize(Stream stream) => _serializer.Deserialize(stream);
	}
}