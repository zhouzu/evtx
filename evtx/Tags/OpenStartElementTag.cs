﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using NLog;

namespace evtx.Tags
{
    public class OpenStartElementTag : IBinXml
    {
        public StringTableEntry Name { get; }

        private readonly ChunkInfo _chunk;

        public OpenStartElementTag(long recordPosition, BinaryReader dataStream, ChunkInfo chunk, bool hasAttribute)
        {
            var l = LogManager.GetLogger("BuildTag");

            _chunk = chunk;

            RecordPosition = recordPosition;

            Nodes = new List<IBinXml>();
            Attributes = new List<Attribute>();

            SubstitutionSlot = dataStream.ReadInt16();

            Size = dataStream.ReadInt32();

            var startPos = dataStream.BaseStream.Position;

            var elementOffset = dataStream.ReadUInt32();

            Name = chunk.GetStringTableEntry(elementOffset);

            var subinfo = string.Empty;
            if (SubstitutionSlot > -1)
            {
                subinfo = $", Substitution Slot: {SubstitutionSlot}";
            }

            if (elementOffset > recordPosition + startPos)
            {
                dataStream.BaseStream.Seek(Name.Size, SeekOrigin.Current);
            }

            if (hasAttribute)
            {
                var attrSize = dataStream.ReadInt32();
                var attrStartPos = dataStream.BaseStream.Position;

                while (dataStream.BaseStream.Position < attrStartPos + attrSize)
                {
                    var attrTag = TagBuilder.BuildTag(recordPosition, dataStream, chunk);

                    if (attrTag is Attribute attribute)
                    {
                        Attributes.Add(attribute);
                    }
                }
            }

            var i = TagBuilder.BuildTag(recordPosition, dataStream, chunk);

            Trace.Assert(i is CloseStartElementTag || i is CloseEmptyElementTag, "I didn't get a CloseStartElementTag");

            Nodes.Add(i);

            var att = string.Empty;
            if (Attributes.Count > 0)
            {
                att = $", attributes: {string.Join(" | ", Attributes)}";
            }

            l.Trace($"Name: {Name.Value}{subinfo}{att}");

            while (dataStream.BaseStream.Position < startPos + Size)
            {
                var n = TagBuilder.BuildTag(recordPosition, dataStream, chunk);
                Nodes.Add(n);
            }
        }

        public List<Attribute> Attributes { get; set; }
        public List<IBinXml> Nodes { get; set; }
        public int SubstitutionSlot { get; }

        public long RecordPosition { get; }
        public long Size { get; }

        public TagBuilder.BinaryTag TagType => TagBuilder.BinaryTag.OpenStartElementTag;
        public string AsXml(List<SubstitutionArrayEntry> substitutionEntries)
        {
            var sb = new StringBuilder();

            sb.Append($"<{Name.Value}");

            var attrStrings = new List<string>();

            foreach (var attribute in Attributes)
            {
                var attrVal = attribute.AsXml(substitutionEntries);
                if (attrVal.Length > 0)
                {
                    attrStrings.Add(attrVal);
                }
            }

            if (attrStrings.Count > 0) 
            {
                //at least one attribute with a value
                sb.Append(" " + string.Join(" ",attrStrings));
            }

            foreach (var node in Nodes)
            {
                if (node is EndElementTag)
                {
                    sb.AppendLine( $"</{Name.Value}>");
                }
                else if (node is CloseEmptyElementTag)
                {
                    sb.AppendLine(node.AsXml(substitutionEntries));
                }
                else if (node is CloseStartElementTag )
                {
                    sb.Append(node.AsXml(substitutionEntries));
                } 
                else
                {
                    if (Name.Value == "Keywords")
                    {
                        var kw = (OptionalSubstitution) node;
                        var kwVal = BitConverter.ToUInt64(
                            substitutionEntries.Single(t => t.Position == kw.SubstitutionId).DataBytes, 0);

                        sb.Append($"{TagBuilder.GetKeywordDescription(kwVal)}");
                    }
                    else if (node is OptionalSubstitution || node is NormalSubstitution)
                    {
                        if (node is OptionalSubstitution os)
                        {
                            if (os.ValueType == TagBuilder.ValueType.BinXmlType)
                            {
                                var osData = substitutionEntries.Single(t => t.Position == os.SubstitutionId);
                                 var ms = new MemoryStream(osData.DataBytes);
                                 var br = new BinaryReader(ms);
                          
                                var eof = false;

                                while (br.BaseStream.Position < br.BaseStream.Length)
                                {
                                    var nextTag = TagBuilder.BuildTag(RecordPosition, br, _chunk);
                                 
                                    if (nextTag is TemplateInstance te)
                                    {
                                        sb.AppendLine(te.AsXml(te.SubstitutionEntries));
                                        eof = true;
                                    }

                                    if (nextTag is EndOfBXmlStream)
                                        //nothing left to do, so exit
                                    {
                                        break;
                                    }
                                 
                                }

                            }
                            else
                            {
                                sb.Append(node.AsXml(substitutionEntries));
                            }
                        }
                        else
                        {
                            var ns = (NormalSubstitution) node;
                            {
                                if (ns.ValueType == TagBuilder.ValueType.BinXmlType)
                                {
                                    sb.AppendLine("ns BIN XML");
                                }
                            }
                        }
                    }
                    else
                    {
                        sb.Append( node.AsXml(substitutionEntries));         
                    }
                       
                }
            
            }

            return sb.ToString();
        }
    }
}