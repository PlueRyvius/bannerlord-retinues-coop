using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using TaleWorlds.SaveSystem;

namespace Retinues.Mods.Coop
{
    /// <summary>
    /// Turns Retinues' save data (FactionSaveData and everything under it) into a string and back, by the same
    /// [SaveableField] fields the campaign save uses, so a network snapshot always carries exactly what a save
    /// would. Supported field types: string, int, bool, float, enums, lists of those, and nested save classes.
    /// Both sides must run the same build (the co-op hello checks the protocol version).
    /// </summary>
    public static class SaveDataCodec
    {
        private static readonly Dictionary<Type, (int Id, FieldInfo Field)[]> Fields = [];

        public static string Encode(object value)
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream))
                Write(writer, value, value?.GetType() ?? typeof(object));
            return Convert.ToBase64String(stream.ToArray());
        }

        public static T Decode<T>(string text)
            where T : class
        {
            using var reader = new BinaryReader(new MemoryStream(Convert.FromBase64String(text)));
            return (T)Read(reader, typeof(T));
        }

        private static (int Id, FieldInfo Field)[] FieldsOf(Type type)
        {
            lock (Fields)
            {
                if (Fields.TryGetValue(type, out var known))
                    return known;

                var found = type.GetFields(
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                    )
                    .Select(f => (Field: f, Attr: f.GetCustomAttribute<SaveableFieldAttribute>()))
                    .Where(x => x.Attr != null)
                    .Select(x => (Id: (int)x.Attr.LocalSaveId, x.Field))
                    .OrderBy(x => x.Id)
                    .ToArray();
                Fields[type] = found;
                return found;
            }
        }

        private static void Write(BinaryWriter w, object value, Type type)
        {
            if (type == typeof(string))
            {
                w.Write(value != null);
                if (value != null)
                    w.Write((string)value);
            }
            else if (type == typeof(int))
                w.Write((int)value);
            else if (type == typeof(bool))
                w.Write((bool)value);
            else if (type == typeof(float))
                w.Write((float)value);
            else if (type.IsEnum)
                w.Write(Convert.ToInt32(value));
            else if (IsList(type, out var item))
            {
                var list = (IList)value;
                w.Write(list == null ? -1 : list.Count);
                if (list != null)
                    foreach (var element in list)
                        Write(w, element, item);
            }
            else
            {
                w.Write(value != null);
                if (value == null)
                    return;

                var fields = FieldsOf(type);
                w.Write(fields.Length);
                foreach (var (id, field) in fields)
                {
                    w.Write(id);
                    Write(w, field.GetValue(value), field.FieldType);
                }
            }
        }

        private static object Read(BinaryReader r, Type type)
        {
            if (type == typeof(string))
                return r.ReadBoolean() ? r.ReadString() : null;
            if (type == typeof(int))
                return r.ReadInt32();
            if (type == typeof(bool))
                return r.ReadBoolean();
            if (type == typeof(float))
                return r.ReadSingle();
            if (type.IsEnum)
                return Enum.ToObject(type, r.ReadInt32());
            if (IsList(type, out var item))
            {
                var count = r.ReadInt32();
                if (count < 0)
                    return null;
                var list = (IList)Activator.CreateInstance(type);
                for (int i = 0; i < count; i++)
                    list.Add(Read(r, item));
                return list;
            }

            if (!r.ReadBoolean())
                return null;

            // Save classes have parameterless constructors for the save system; field initialisers run there.
            var obj =
                type.GetConstructor(Type.EmptyTypes) != null
                    ? Activator.CreateInstance(type, true)
                    : FormatterServices.GetUninitializedObject(type);

            var byId = FieldsOf(type).ToDictionary(f => f.Id, f => f.Field);
            var n = r.ReadInt32();
            for (int i = 0; i < n; i++)
            {
                var id = r.ReadInt32();
                if (!byId.TryGetValue(id, out var field))
                    throw new InvalidDataException($"{type.Name} has no save field {id}.");
                field.SetValue(obj, Read(r, field.FieldType));
            }
            return obj;
        }

        private static bool IsList(Type type, out Type item)
        {
            item = null;
            if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(List<>))
                return false;
            item = type.GetGenericArguments()[0];
            return true;
        }
    }
}
