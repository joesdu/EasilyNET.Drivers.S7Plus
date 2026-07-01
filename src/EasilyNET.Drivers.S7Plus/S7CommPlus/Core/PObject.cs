// SPDX-License-Identifier: LGPL-3.0-or-later
// Derived from thomas-v2/S7CommPlusDriver, Copyright (C) 2023 Thomas Wiens. See LICENSE-LGPL-3.0.txt.
namespace EasilyNET.Drivers.S7Plus.S7CommPlus.Core;

internal sealed class PObject(uint RID, uint CLSID, uint AID) : IS7pSerialize
{
    public uint RelationId { get; set; } = RID;
    public uint ClassId { get; set; } = CLSID;
    public uint ClassFlags { get; set; }
    public uint AttributeId { get; set; } = AID;
    public Dictionary<uint, PValue> Attributes { get; } = [];
    private Dictionary<Tuple<uint, uint>, PObject> Objects { get; } = [];
    public Dictionary<uint, uint> Relations { get; } = [];
    public PVartypeList? VartypeList { get; private set; }
    public PVarnameList? VarnameList { get; private set; }

    public PObject() : this(0, 0, 0) { }

    public void AddAttribute(uint attributeid, PValue value)
    {
        Attributes.Add(attributeid, value);
    }

    public PValue GetAttribute(uint attributeid)
    {
        return Attributes[attributeid];
    }

    public void AddRelation(uint relationid, uint value)
    {
        Relations.Add(relationid, value);
    }

    public void SetVartypeList(PVartypeList typelist)
    {
        VartypeList = typelist;
    }

    public void SetVarnameList(PVarnameList namelist)
    {
        VarnameList = namelist;
    }

    public void AddObject(PObject? obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        // Whether using the ClassId as Key makes sense, remains to be seen
        // TODO: The ClassId is not unique and may be occur more than once
        // (e.g. DB.Class_Rid and in RelId is the DB number as DB.1)
        var tuple = new Tuple<uint, uint>(obj.ClassId, obj.RelationId);
        Objects.Add(tuple, obj);
    }

    public PObject GetObjectByClassId(uint classId, uint relId)
    {
        var tuple = new Tuple<uint, uint>(classId, relId);
        return Objects[tuple];
    }

    public List<PObject> GetObjectsByClassId(uint classId)
    {
        var objList = new List<PObject>();
        foreach (var obj in Objects)
        {
            if (obj.Key.Item1 == classId)
            {
                objList.Add(obj.Value);
            }
        }
        return objList;
    }

    public List<PObject> GetObjects()
    {
        var objList = new List<PObject>();
        foreach (var obj in Objects)
        {
            objList.Add(obj.Value);
        }
        return objList;
    }

    public int Serialize(Stream buffer)
    {
        var ret = 0;
        ret += S7p.EncodeByte(buffer, ElementID.StartOfObject);
        ret += S7p.EncodeUInt32(buffer, RelationId);
        ret += S7p.EncodeUInt32Vlq(buffer, ClassId);
        ret += S7p.EncodeUInt32Vlq(buffer, ClassFlags);
        ret += S7p.EncodeUInt32Vlq(buffer, AttributeId);
        foreach (var elem in Attributes)
        {
            ret += S7p.EncodeByte(buffer, ElementID.Attribute);
            ret += S7p.EncodeUInt32Vlq(buffer, elem.Key);
            ret += elem.Value.Serialize(buffer);
        }
        foreach (var o in Objects)
        {
            ret += o.Value.Serialize(buffer);
        }
        foreach (var rel in Relations)
        {
            ret += S7p.EncodeByte(buffer, ElementID.Relation);
            ret += S7p.EncodeUInt32Vlq(buffer, rel.Key);
            ret += S7p.EncodeUInt32(buffer, rel.Value);
        }
        ret += S7p.EncodeByte(buffer, ElementID.TerminatingObject);
        return ret;
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine("<Object>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"<RelationId>{RelationId}</RelationId>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"<ClassId>{ClassId}</ClassId>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"<AttributeId>{AttributeId}</AttributeId>");
        foreach (var a in Attributes)
        {
            sb.AppendLine("<Attribute>");
            sb.AppendLine(CultureInfo.InvariantCulture, $"<ID>{a.Key}</ID>");
            sb.AppendLine(a.Value.ToString());
            sb.AppendLine("</Attribute>");
        }
        if (VartypeList != null)
        {
            sb.Append(VartypeList.ToString());
        }
        if (VarnameList != null)
        {
            sb.Append(VarnameList.ToString());
        }
        foreach (var o in Objects)
        {
            sb.Append(o.Value.ToString());
        }
        foreach (var rel in Relations)
        {
            sb.AppendLine("<Relation>");
            sb.AppendLine(CultureInfo.InvariantCulture, $"<ID>{rel.Key}</ID>");
            sb.AppendLine(rel.Value.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("</Relation>");
        }
        sb.AppendLine("</Object>");
        return sb.ToString();
    }
}
