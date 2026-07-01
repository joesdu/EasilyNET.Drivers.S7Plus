// SPDX-License-Identifier: LGPL-3.0-or-later
// Derived from thomas-v2/S7CommPlusDriver, Copyright (C) 2023 Thomas Wiens. See LICENSE-LGPL-3.0.txt.
using EasilyNET.Drivers.S7Plus.S7CommPlus.Core;
using System.Globalization;

namespace EasilyNET.Drivers.S7Plus.S7CommPlus.ClientApi;

internal sealed class Browser
{
    private readonly VarRoot m_Root;
    private List<PObject> m_objs;
    private List<VarInfo> m_varInfoList;

    public Browser()
    {
        m_Root = new VarRoot();
        m_objs = [];
        m_varInfoList = [];
    }

    public List<VarInfo> VarInfoList => m_varInfoList ?? [];

    public void SetTypeInfoContainerObjects(List<PObject> objs)
    {
        m_objs = objs;
    }

    public void AddBlockNode(ENodeType nodetype, string name, uint accessid, uint ti_rel_id)
    {
        var db = new Node
        {
            NodeType = nodetype,
            Name = name,
            AccessId = accessid,
            RelationId = ti_rel_id
        };
        m_Root.Nodes.Add(db);
    }

    public void BuildFlatList()
    {
        m_varInfoList = [];
        foreach (var node in m_Root.Nodes)
        {
            // Skip empty lists in any area like marker or timers.
            if (node.Childs.Count > 0)
            {
                uint OptOffset = 0;
                uint NonOptOffset = 0;
                AddFlatSubnodes(node, string.Empty, string.Empty, OptOffset, NonOptOffset);
            }
        }
    }

    private void AddFlatSubnodes(Node node, string names, string accessIds, uint OptOffset, uint NonOptOffset)
    {
        switch (node.NodeType)
        {
            case ENodeType.Root:
                names += node.Name;
                accessIds += $"{node.AccessId:X}";
                break;
            case ENodeType.Array:
                names += node.Name;
                accessIds += "." + $"{node.AccessId:X}";
                break;
            case ENodeType.StructArray:
                names += node.Name;
                // TODO: Special: Between an array-index and the access-id is an additional 1. It's not known if it's a fixed or variable value.
                accessIds += "." + $"{node.AccessId:X}" + ".1";
                break;
            case ENodeType.Undefined:
            case ENodeType.Var:
            default:
                names += "." + node.Name;
                accessIds += "." + $"{node.AccessId:X}";
                break;
        }

        if (node.Childs.Count == 0)
        {
            // We are at the leaf of our tree
            if (IsSoftdatatypeSupported(node.Softdatatype))
            {
                var info = new VarInfo
                {
                    Name = names,
                    AccessSequence = accessIds,
                    Softdatatype = node.Softdatatype,
                };
                // If an Array element of basic datatype, the Vte is here from the parent array base element and offsets not valid here.
                if (node.NodeType == ENodeType.Array)
                {
                    info.OptAddress = OptOffset;
                    info.NonOptAddress = NonOptOffset;
                }
                else
                {
                    info.OptAddress = OptOffset + node.Vte.OffsetInfoType!.OptimizedAddress;
                    info.NonOptAddress = NonOptOffset + node.Vte.OffsetInfoType.NonoptimizedAddress;
                }
                // Special case #1:
                // There is a strange behaviour when transmitting bitoffsets in not-optmized DBs.
                // If a bool is inside a struct, the offsetinformation is in the attributes (last 3 bits.
                // Bitoffsetinfo bit classic is false in this case.
                // Don't know if this a bug in Plcsim (where I tested with) or intentional.
                //
                // Special case #2:
                // System datatypes like IEC_COUNTER, etc. have Bools with Bitoffsets, even when they are locates in optimized DBs.
                // The bitoffset is then located in the Attributes and not in the bitoffset
                if (node.Softdatatype == Softdatatype.S7COMMP_SOFTDATATYPE_BOOL)
                {
                    info.OptBitoffset = node.Vte.AttributeBitoffset;
                    info.NonOptBitoffset = node.Vte.BitoffsetinfoFlagClassic ? node.Vte.BitoffsetinfoNonoptimizedBitoffset : node.Vte.AttributeBitoffset;
                }
                else if (node.Softdatatype == Softdatatype.S7COMMP_SOFTDATATYPE_BBOOL)
                {
                    info.OptBitoffset = node.Vte.BitoffsetinfoOptimizedBitoffset;
                }
                else
                {
                    info.OptBitoffset = 0;
                    info.NonOptBitoffset = 0;
                }

                m_varInfoList.Add(info);
            }
        }
        else
        {
            // root node (the DB itself) has no VarTypeListElement, but we don't need it here.
            if (node.Vte != null)
            {
                switch (node.NodeType)
                {
                    case ENodeType.Array:
                        // This is an array element of basic datatype. Offset comes from fixed size multiplied by array index.
                        OptOffset = node.Vte.OffsetInfoType!.OptimizedAddress;
                        NonOptOffset = node.Vte.OffsetInfoType.NonoptimizedAddress;
                        break;
                    case ENodeType.StructArray:
                        OptOffset += node.ArrayAdrOffsetOpt;
                        NonOptOffset += node.ArrayAdrOffsetNonOpt;
                        break;
                    case ENodeType.Undefined:
                    case ENodeType.Root:
                    case ENodeType.Var:
                    default:
                        OptOffset += node.Vte.OffsetInfoType!.OptimizedAddress;
                        NonOptOffset += node.Vte.OffsetInfoType.NonoptimizedAddress;
                        break;
                }
            }
            foreach (var sub in node.Childs)
            {
                if (sub.NodeType == ENodeType.Array)
                {
                    AddFlatSubnodes(sub, names, accessIds, OptOffset + sub.ArrayAdrOffsetOpt, NonOptOffset + sub.ArrayAdrOffsetNonOpt);
                }
                else
                {
                    AddFlatSubnodes(sub, names, accessIds, OptOffset, NonOptOffset);
                }
            }
        }
    }

    public void BuildTree()
    {
        if (m_objs == null)
        {
            return;
        }

        for (var i = 0; i < m_Root.Nodes.Count; i++)
        {
            foreach (var ob in m_objs)
            {
                if (ob.RelationId == m_Root.Nodes[i].RelationId)
                {
                    var node = m_Root.Nodes[i];
                    AddSubNodes(ref node, ob);
                    break;
                }
            }
        }
    }

    private void AddSubNodes(ref Node node, PObject o)
    {
        uint ArrayElementCount;
        int ArrayLowerBounds;
        uint[] MdimArrayElementCount;
        int[] MdimArrayLowerBounds;

        var element_index = 0;
        uint TComSize;

        // If there are no variables at all in an area, then this list does not exist (no error).
        if (o.VartypeList != null && o.VarnameList != null)
        {
            foreach (var vte in o.VartypeList.Elements)
            {
                var subnode = new Node
                {
                    Name = o.VarnameList.Names[element_index],
                    Softdatatype = vte.Softdatatype,
                    AccessId = vte.LID,
                    Vte = vte,
                };

                node.Childs.Add(subnode);
                // Process arrays. TODO: Put the processing to separate methods, to shorten this method.
                if (vte.OffsetInfoType!.Is1Dim())
                {
                    #region Struct/UDT or flat arrays with one dimension
                    var ioit = (IOffsetInfoType_1Dim)vte.OffsetInfoType;
                    ArrayElementCount = ioit.GetArrayElementCount();
                    ArrayLowerBounds = ioit.GetArrayLowerBounds();

                    // The access-id always starts with 0, independent of lowerbounds
                    for (uint i = 0; i < ArrayElementCount; i++)
                    {
                        // Handle Struct/FB Array separate: Has an additional ID between array index and access-LID.
                        if (vte.OffsetInfoType.HasRelation())
                        {
                            var arraynode = new Node
                            {
                                NodeType = ENodeType.StructArray,
                                Name = "[" + (i + ArrayLowerBounds) + "]",
                                Softdatatype = vte.Softdatatype,
                                AccessId = i,
                                Vte = vte,
                            };
                            subnode.Childs.Add(arraynode);

                            // All OffsetInfoTypes which occur at this point should have a Relation Id
                            var ioit2 = (IOffsetInfoType_Relation)vte.OffsetInfoType;

                            foreach (var ob in m_objs)
                            {
                                if (ob.RelationId == ioit2.GetRelationId())
                                {
                                    // Get the size of a struct element
                                    TComSize = ((ValueUDInt)ob.GetAttribute(Ids.TI_TComSize)).Value;
                                    arraynode.ArrayAdrOffsetOpt = i * TComSize;
                                    arraynode.ArrayAdrOffsetNonOpt = i * TComSize;

                                    AddSubNodes(ref arraynode, ob);
                                    break;
                                }
                            }
                        }
                        else
                        {
                            var arraynode = new Node
                            {
                                NodeType = ENodeType.Array,
                                Name = "[" + (i + ArrayLowerBounds) + "]",
                                Softdatatype = vte.Softdatatype,
                                AccessId = i,
                                Vte = vte,
                            };
                            // Get the size of the basic datatype
                            TComSize = GetSizeOfDatatype(vte);
                            arraynode.ArrayAdrOffsetOpt = i * TComSize;
                            arraynode.ArrayAdrOffsetNonOpt = i * TComSize;

                            subnode.Childs.Add(arraynode);
                        }
                    }
                    #endregion
                }
                else if (vte.OffsetInfoType.IsMDim())
                {
                    #region Struct/UDT or flat array with more than one dimension
                    var ioit = (IOffsetInfoType_MDim)vte.OffsetInfoType;
                    ArrayElementCount = ioit.GetArrayElementCount();
                    ArrayLowerBounds = ioit.GetArrayLowerBounds();
                    MdimArrayElementCount = ioit.GetMdimArrayElementCount();
                    MdimArrayLowerBounds = ioit.GetMdimArrayLowerBounds();

                    // Determine the actual number of dimensions
                    var actdimensions = 0;
                    for (var d = 0; d < 6; d++)
                    {
                        if (MdimArrayElementCount[d] > 0)
                        {
                            actdimensions++;
                        }
                    }

                    var aname = "";
                    uint n = 1;
                    uint id = 0;
                    var xx = new uint[6] { 0, 0, 0, 0, 0, 0 };
                    do
                    {
                        aname = "[";
                        for (var j = actdimensions - 1; j >= 0; j--)
                        {
                            aname += (xx[j] + MdimArrayLowerBounds[j]).ToString(CultureInfo.InvariantCulture);
                            if (j > 0)
                            {
                                aname += ",";
                            }
                            else
                            {
                                aname += "]";
                            }
                        }

                        if (vte.OffsetInfoType.HasRelation())
                        {
                            var arraynode = new Node
                            {
                                NodeType = ENodeType.StructArray,
                                Name = aname,
                                Softdatatype = vte.Softdatatype,
                                AccessId = id,
                                Vte = vte,
                            };
                            subnode.Childs.Add(arraynode);

                            // All OffsetInfoTypes which occur at this point should have a Relation Id
                            var ioit2 = (IOffsetInfoType_Relation)vte.OffsetInfoType;
                            foreach (var ob in m_objs.Where(ob => ob.RelationId == ioit2.GetRelationId()))
                            {
                                // Get the size of a struct element
                                TComSize = ((ValueUDInt)ob.GetAttribute(Ids.TI_TComSize)).Value;
                                arraynode.ArrayAdrOffsetOpt = (n - 1) * TComSize;
                                arraynode.ArrayAdrOffsetNonOpt = (n - 1) * TComSize;

                                AddSubNodes(ref arraynode, ob);
                                break;
                            }
                        }
                        else
                        {
                            var arraynode = new Node
                            {
                                NodeType = ENodeType.Array,
                                Name = aname,
                                Softdatatype = vte.Softdatatype,
                                AccessId = id,
                                Vte = vte,
                            };
                            TComSize = GetSizeOfDatatype(vte);
                            arraynode.ArrayAdrOffsetOpt = (n - 1) * TComSize;
                            arraynode.ArrayAdrOffsetNonOpt = (n - 1) * TComSize;

                            subnode.Childs.Add(arraynode);
                        }
                        xx[0]++;
                        // BBOOL-Arrays on overflow the ID of the lowest array index goes only up to 8.
                        if (subnode.Softdatatype == Softdatatype.S7COMMP_SOFTDATATYPE_BBOOL && xx[0] >= MdimArrayElementCount[0])
                        {
                            if (MdimArrayElementCount[0] % 8 != 0)
                            {
                                id += 8 - (xx[0] % 8);
                            }
                        }
                        for (var dim = 0; dim < 5; dim++)
                        {
                            if (xx[dim] >= MdimArrayElementCount[dim])
                            {
                                xx[dim] = 0;
                                xx[dim + 1]++;
                            }
                        }
                        id++;
                        n++;
                    } while (n <= ArrayElementCount);
                    #endregion
                }
                else if (vte.OffsetInfoType.HasRelation())
                {
                    #region Struct / UDT / system library types (DTL, IEC_TIMER, ...) but not an array ...
                    var ioit = (IOffsetInfoType_Relation)vte.OffsetInfoType;

                    foreach (var ob in m_objs)
                    {
                        if (ob.RelationId == ioit.GetRelationId())
                        {
                            AddSubNodes(ref subnode, ob);
                            break;
                        }
                    }
                    // Empty areas are allowed, so don't return this as an error.
                    #endregion
                }
                element_index++;
            }
        }
    }

    private static uint GetSizeOfDatatype(PVartypeListElement vte)
    {
        // Returns the size of an element if stored as an array
        return vte.Softdatatype switch
        {
            Softdatatype.S7COMMP_SOFTDATATYPE_BOOL => 1,// TODO: Bit Bool?
            Softdatatype.S7COMMP_SOFTDATATYPE_BYTE => 1,
            Softdatatype.S7COMMP_SOFTDATATYPE_CHAR => 1,
            Softdatatype.S7COMMP_SOFTDATATYPE_WORD => 2,
            Softdatatype.S7COMMP_SOFTDATATYPE_INT => 2,
            Softdatatype.S7COMMP_SOFTDATATYPE_DWORD => 4,
            Softdatatype.S7COMMP_SOFTDATATYPE_DINT => 4,
            Softdatatype.S7COMMP_SOFTDATATYPE_REAL => 4,
            Softdatatype.S7COMMP_SOFTDATATYPE_DATE => 2,
            Softdatatype.S7COMMP_SOFTDATATYPE_TIMEOFDAY => 4,
            Softdatatype.S7COMMP_SOFTDATATYPE_TIME => 4,
            Softdatatype.S7COMMP_SOFTDATATYPE_S5TIME => 2,
            Softdatatype.S7COMMP_SOFTDATATYPE_DATEANDTIME => 8,
            Softdatatype.S7COMMP_SOFTDATATYPE_STRING or Softdatatype.S7COMMP_SOFTDATATYPE_WSTRING => vte.OffsetInfoType!.Is1Dim()
                                ? ((POffsetInfoType_Array1Dim)vte.OffsetInfoType).UnspecifiedOffsetinfo1 + (uint)2
                                : ((POffsetInfoType_ArrayMDim)vte.OffsetInfoType).UnspecifiedOffsetinfo1 + (uint)2,// TODO:
                                                                                                                   // If an array of String or WString, offsetinfo1 is the string length.
                                                                                                                   // First though was, that offsetinfo2 is length including header of 2 bytes.
                                                                                                                   // but with an Multidim Array [0..2, 0..1] of String[5] offsetinfo is 8, which is not
                                                                                                                   // correct when you look at the data.
                                                                                                                   // Tested only with Plcsim, which may be a bug in Plcsim?
            Softdatatype.S7COMMP_SOFTDATATYPE_POINTER => 6,
            Softdatatype.S7COMMP_SOFTDATATYPE_ANY => 10,
            Softdatatype.S7COMMP_SOFTDATATYPE_BLOCKFB => 2,
            Softdatatype.S7COMMP_SOFTDATATYPE_BLOCKFC => 2,
            Softdatatype.S7COMMP_SOFTDATATYPE_COUNTER => 2,
            Softdatatype.S7COMMP_SOFTDATATYPE_TIMER => 2,
            Softdatatype.S7COMMP_SOFTDATATYPE_BBOOL => 1,// Bool of size 1 byte here
            Softdatatype.S7COMMP_SOFTDATATYPE_LREAL => 8,
            Softdatatype.S7COMMP_SOFTDATATYPE_ULINT => 8,
            Softdatatype.S7COMMP_SOFTDATATYPE_LINT => 8,
            Softdatatype.S7COMMP_SOFTDATATYPE_LWORD => 8,
            Softdatatype.S7COMMP_SOFTDATATYPE_USINT => 1,
            Softdatatype.S7COMMP_SOFTDATATYPE_UINT => 2,
            Softdatatype.S7COMMP_SOFTDATATYPE_UDINT => 4,
            Softdatatype.S7COMMP_SOFTDATATYPE_SINT => 1,
            Softdatatype.S7COMMP_SOFTDATATYPE_WCHAR => 2,
            Softdatatype.S7COMMP_SOFTDATATYPE_LTIME => 8,
            Softdatatype.S7COMMP_SOFTDATATYPE_LTOD => 8,
            Softdatatype.S7COMMP_SOFTDATATYPE_LDT => 8,
            Softdatatype.S7COMMP_SOFTDATATYPE_DTL => 12,// In most cases as a struct of system type
            Softdatatype.S7COMMP_SOFTDATATYPE_REMOTE => 10,
            Softdatatype.S7COMMP_SOFTDATATYPE_AOMIDENT => 4,
            Softdatatype.S7COMMP_SOFTDATATYPE_EVENTANY => 4,
            Softdatatype.S7COMMP_SOFTDATATYPE_EVENTATT => 4,
            Softdatatype.S7COMMP_SOFTDATATYPE_AOMAID => 0,// TODO: Not possible to define this type
            Softdatatype.S7COMMP_SOFTDATATYPE_AOMLINK => 0,// TODO: Not possible to define this type
            Softdatatype.S7COMMP_SOFTDATATYPE_EVENTHWINT => 4,
            Softdatatype.S7COMMP_SOFTDATATYPE_HWANY or Softdatatype.S7COMMP_SOFTDATATYPE_HWIOSYSTEM or Softdatatype.S7COMMP_SOFTDATATYPE_HWDPMASTER or Softdatatype.S7COMMP_SOFTDATATYPE_HWDEVICE or Softdatatype.S7COMMP_SOFTDATATYPE_HWDPSLAVE or Softdatatype.S7COMMP_SOFTDATATYPE_HWIO or Softdatatype.S7COMMP_SOFTDATATYPE_HWMODULE or Softdatatype.S7COMMP_SOFTDATATYPE_HWSUBMODULE or Softdatatype.S7COMMP_SOFTDATATYPE_HWHSC or Softdatatype.S7COMMP_SOFTDATATYPE_HWPWM or Softdatatype.S7COMMP_SOFTDATATYPE_HWPTO or Softdatatype.S7COMMP_SOFTDATATYPE_HWINTERFACE or Softdatatype.S7COMMP_SOFTDATATYPE_HWIEPORT => 2,
            Softdatatype.S7COMMP_SOFTDATATYPE_OBANY or Softdatatype.S7COMMP_SOFTDATATYPE_OBDELAY or Softdatatype.S7COMMP_SOFTDATATYPE_OBTOD or Softdatatype.S7COMMP_SOFTDATATYPE_OBCYCLIC or Softdatatype.S7COMMP_SOFTDATATYPE_OBATT => 2,
            Softdatatype.S7COMMP_SOFTDATATYPE_CONNANY => 2,
            Softdatatype.S7COMMP_SOFTDATATYPE_CONNPRG => 2,
            Softdatatype.S7COMMP_SOFTDATATYPE_CONNOUC => 2,
            Softdatatype.S7COMMP_SOFTDATATYPE_CONNRID => 4,
            Softdatatype.S7COMMP_SOFTDATATYPE_PORT => 2,
            Softdatatype.S7COMMP_SOFTDATATYPE_RTM => 2,
            Softdatatype.S7COMMP_SOFTDATATYPE_PIP => 2,
            Softdatatype.S7COMMP_SOFTDATATYPE_OBPCYCLE => 2,
            Softdatatype.S7COMMP_SOFTDATATYPE_OBHWINT => 2,
            Softdatatype.S7COMMP_SOFTDATATYPE_OBDIAG => 2,
            Softdatatype.S7COMMP_SOFTDATATYPE_OBTIMEERROR => 2,
            Softdatatype.S7COMMP_SOFTDATATYPE_OBSTARTUP => 2,
            Softdatatype.S7COMMP_SOFTDATATYPE_DBANY => 2,
            Softdatatype.S7COMMP_SOFTDATATYPE_DBWWW => 2,
            Softdatatype.S7COMMP_SOFTDATATYPE_DBDYN => 2,
            _ => 0,
        };
    }

    private static bool IsSoftdatatypeSupported(uint softdatatype)
    {
        return softdatatype switch
        {
            Softdatatype.S7COMMP_SOFTDATATYPE_BOOL or Softdatatype.S7COMMP_SOFTDATATYPE_BYTE or Softdatatype.S7COMMP_SOFTDATATYPE_CHAR or Softdatatype.S7COMMP_SOFTDATATYPE_WORD or Softdatatype.S7COMMP_SOFTDATATYPE_INT or Softdatatype.S7COMMP_SOFTDATATYPE_DWORD or Softdatatype.S7COMMP_SOFTDATATYPE_DINT or Softdatatype.S7COMMP_SOFTDATATYPE_REAL or Softdatatype.S7COMMP_SOFTDATATYPE_DATE or Softdatatype.S7COMMP_SOFTDATATYPE_TIMEOFDAY or Softdatatype.S7COMMP_SOFTDATATYPE_TIME or Softdatatype.S7COMMP_SOFTDATATYPE_S5TIME or Softdatatype.S7COMMP_SOFTDATATYPE_DATEANDTIME or Softdatatype.S7COMMP_SOFTDATATYPE_STRING or Softdatatype.S7COMMP_SOFTDATATYPE_POINTER or Softdatatype.S7COMMP_SOFTDATATYPE_ANY or Softdatatype.S7COMMP_SOFTDATATYPE_BLOCKFB or Softdatatype.S7COMMP_SOFTDATATYPE_BLOCKFC or Softdatatype.S7COMMP_SOFTDATATYPE_COUNTER or Softdatatype.S7COMMP_SOFTDATATYPE_TIMER or Softdatatype.S7COMMP_SOFTDATATYPE_BBOOL or Softdatatype.S7COMMP_SOFTDATATYPE_LREAL or Softdatatype.S7COMMP_SOFTDATATYPE_ULINT or Softdatatype.S7COMMP_SOFTDATATYPE_LINT or Softdatatype.S7COMMP_SOFTDATATYPE_LWORD or Softdatatype.S7COMMP_SOFTDATATYPE_USINT or Softdatatype.S7COMMP_SOFTDATATYPE_UINT or Softdatatype.S7COMMP_SOFTDATATYPE_UDINT or Softdatatype.S7COMMP_SOFTDATATYPE_SINT or Softdatatype.S7COMMP_SOFTDATATYPE_WCHAR or Softdatatype.S7COMMP_SOFTDATATYPE_WSTRING or Softdatatype.S7COMMP_SOFTDATATYPE_LTIME or Softdatatype.S7COMMP_SOFTDATATYPE_LTOD or Softdatatype.S7COMMP_SOFTDATATYPE_LDT or Softdatatype.S7COMMP_SOFTDATATYPE_DTL or Softdatatype.S7COMMP_SOFTDATATYPE_REMOTE or Softdatatype.S7COMMP_SOFTDATATYPE_AOMIDENT or Softdatatype.S7COMMP_SOFTDATATYPE_EVENTANY or Softdatatype.S7COMMP_SOFTDATATYPE_EVENTATT or Softdatatype.S7COMMP_SOFTDATATYPE_AOMAID or Softdatatype.S7COMMP_SOFTDATATYPE_AOMLINK or Softdatatype.S7COMMP_SOFTDATATYPE_EVENTHWINT or Softdatatype.S7COMMP_SOFTDATATYPE_HWANY or Softdatatype.S7COMMP_SOFTDATATYPE_HWIOSYSTEM or Softdatatype.S7COMMP_SOFTDATATYPE_HWDPMASTER or Softdatatype.S7COMMP_SOFTDATATYPE_HWDEVICE or Softdatatype.S7COMMP_SOFTDATATYPE_HWDPSLAVE or Softdatatype.S7COMMP_SOFTDATATYPE_HWIO or Softdatatype.S7COMMP_SOFTDATATYPE_HWMODULE or Softdatatype.S7COMMP_SOFTDATATYPE_HWSUBMODULE or Softdatatype.S7COMMP_SOFTDATATYPE_HWHSC or Softdatatype.S7COMMP_SOFTDATATYPE_HWPWM or Softdatatype.S7COMMP_SOFTDATATYPE_HWPTO or Softdatatype.S7COMMP_SOFTDATATYPE_HWINTERFACE or Softdatatype.S7COMMP_SOFTDATATYPE_HWIEPORT or Softdatatype.S7COMMP_SOFTDATATYPE_OBANY or Softdatatype.S7COMMP_SOFTDATATYPE_OBDELAY or Softdatatype.S7COMMP_SOFTDATATYPE_OBTOD or Softdatatype.S7COMMP_SOFTDATATYPE_OBCYCLIC or Softdatatype.S7COMMP_SOFTDATATYPE_OBATT or Softdatatype.S7COMMP_SOFTDATATYPE_CONNANY or Softdatatype.S7COMMP_SOFTDATATYPE_CONNPRG or Softdatatype.S7COMMP_SOFTDATATYPE_CONNOUC or Softdatatype.S7COMMP_SOFTDATATYPE_CONNRID or Softdatatype.S7COMMP_SOFTDATATYPE_PORT or Softdatatype.S7COMMP_SOFTDATATYPE_RTM or Softdatatype.S7COMMP_SOFTDATATYPE_PIP or Softdatatype.S7COMMP_SOFTDATATYPE_OBPCYCLE or Softdatatype.S7COMMP_SOFTDATATYPE_OBHWINT or Softdatatype.S7COMMP_SOFTDATATYPE_OBDIAG or Softdatatype.S7COMMP_SOFTDATATYPE_OBTIMEERROR or Softdatatype.S7COMMP_SOFTDATATYPE_OBSTARTUP or Softdatatype.S7COMMP_SOFTDATATYPE_DBANY or Softdatatype.S7COMMP_SOFTDATATYPE_DBWWW or Softdatatype.S7COMMP_SOFTDATATYPE_DBDYN => true,
            _ => false,
        };
    }

    internal class Node
    {
        public ENodeType NodeType { get; set; } = ENodeType.Undefined;
        public string Name { get; set; } = string.Empty;
        public uint AccessId { get; set; }
        public uint Softdatatype { get; set; }
        public uint RelationId { get; set; }
        public PVartypeListElement Vte { get; set; } = new();
        public uint ArrayAdrOffsetOpt { get; set; }    // Offset of an Element when it's an array, optimized
        public uint ArrayAdrOffsetNonOpt { get; set; } // Offset of an Element when it's an array, not-optimized

        public List<Node> Childs { get; private set; } = [];
    }

    internal class VarRoot
    {
        public List<Node> Nodes { get; private set; } = [];
    }
}

internal sealed class VarInfo
{
    public string Name { get; set; } = string.Empty;
    public string AccessSequence { get; set; } = string.Empty;
    public uint Softdatatype { get; set; }
    public uint OptAddress { get; set; }       // Optimized access: Byte-Offset where the value is located when reading a complete DB content.
    public int OptBitoffset { get; set; }        // Optimized access: Bit-Offset where the value is located when reading a complete DB content. 
    public uint NonOptAddress { get; set; }    // NonOptimized access: Byte-Offset where the value is located when reading a complete DB content.
    public int NonOptBitoffset { get; set; }     // NonOptimized access: Bit-Offset where the value is located when reading a complete DB content.
}

internal enum ENodeType
{
    Undefined = 0,
    Root,
    Var,
    Array,
    StructArray
}
