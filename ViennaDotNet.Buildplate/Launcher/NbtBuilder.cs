using Cyotek.Data.Nbt;
using Cyotek.Data.Nbt.Serialization;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using ViennaDotNet.Common.Utils;

namespace ViennaDotNet.Buildplate.Launcher
{
    sealed class NbtBuilder
    {
        public sealed class Compound
        {
            private readonly LinkedList<Tag> tags = new LinkedList<Tag>();

            public Compound()
            {
                // empty
            }

            public TagCompound build(string name)
            {
                TagCompound tag = new TagCompound(name);
                tags.ForEach(tag.Value.Add);
                return tag;
            }

            public Compound put(string name, int value)
            {
                TagInt tag = new TagInt(name, value);
                tags.AddLast(tag);
                return this;
            }

            public Compound put(string name, byte value)
            {
                TagByte tag = new TagByte(name, value);
                tags.AddLast(tag);
                return this;
            }

            public Compound put(string name, short value)
            {
                TagShort tag = new TagShort(name, value);
                tags.AddLast(tag);
                return this;
            }

            public Compound put(string name, long value)
            {
                TagLong tag = new TagLong(name, value);
                tags.AddLast(tag);
                return this;
            }

            public Compound put(string name, float value)
            {
                TagFloat tag = new TagFloat(name, value);
                tags.AddLast(tag);
                return this;
            }

            public Compound put(string name, double value)
            {
                TagDouble tag = new TagDouble(name, value);
                tags.AddLast(tag);
                return this;
            }


            public Compound put(string name, string value)
            {
                TagString tag = new TagString(name, value);
                tags.AddLast(tag);
                return this;
            }


            public Compound put(string name, int[] value)
            {
                TagIntArray tag = new TagIntArray(name, value);
                tags.AddLast(tag);
                return this;
            }


            public Compound put(string name, byte[] value)
            {
                TagByteArray tag = new TagByteArray(name, value);
                tags.AddLast(tag);
                return this;
            }


            public Compound put(string name, long[] value)
            {
                throw new NotImplementedException();
                //LongArrayTag tag = new LongArrayTag(name);
                //tag.setValue(value);
                //tags.add(tag);
                //return this;
            }


            public Compound put(string name, Compound value)
            {
                TagCompound tag = value.build(name);
                tags.AddLast(tag);
                return this;
            }


            public Compound put(string name, List value)
            {
                TagList tag = value.build(name);
                tags.AddLast(tag);
                return this;
            }
        }

        public sealed class List
        {
            private readonly TagType type;
            private readonly LinkedList<Tag> tags = new LinkedList<Tag>();

            public List(TagType type)
            {
                this.type = type;
            }

            public TagList build(string name)
            {
                TagList tag = new TagList(name, type);
                tags.ForEach(tag.Value.Add);
                return tag;
            }

            public List add(int value)
            {
                TagInt tag = new TagInt("", value);
                tags.AddLast(tag);
                return this;
            }

            public List add(byte value)
            {
                TagByte tag = new TagByte("", value);
                tags.AddLast(tag);
                return this;
            }

            public List add(short value)
            {
                TagShort tag = new TagShort("", value);
                tags.AddLast(tag);
                return this;
            }

            public List add(long value)
            {
                TagLong tag = new TagLong("", value);
                tags.AddLast(tag);
                return this;
            }

            public List add(float value)
            {
                TagFloat tag = new TagFloat("", value);
                tags.AddLast(tag);
                return this;
            }

            public List add(double value)
            {
                TagDouble tag = new TagDouble("", value);
                tags.AddLast(tag);
                return this;
            }

            public List add(string value)
            {
                TagString tag = new TagString("", value);
                tags.AddLast(tag);
                return this;
            }

            public List add(int[] value)
            {
                TagIntArray tag = new TagIntArray("", value);
                tags.AddLast(tag);
                return this;
            }

            public List add(byte[] value)
            {
                TagByteArray tag = new TagByteArray("", value);
                tags.AddLast(tag);
                return this;
            }

            public List add(long[] value)
            {
                throw new NotImplementedException();
                //LongArrayTag tag = new LongArrayTag("");
                //tag.setValue(value);
                //this.tags.add(tag);
                //return this;
            }

            public List add(Compound value)
            {
                TagCompound tag = value.build("");
                tags.AddLast(tag);
                return this;
            }

            public List add(List value)
            {
                TagList tag = value.build("");
                tags.AddLast(tag);
                return this;
            }
        }
    }
}
