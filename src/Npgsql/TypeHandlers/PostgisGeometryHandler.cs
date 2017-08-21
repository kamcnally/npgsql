﻿#region License
// The PostgreSQL License
//
// Copyright (C) 2017 The Npgsql Development Team
//
// Permission to use, copy, modify, and distribute this software and its
// documentation for any purpose, without fee, and without a written
// agreement is hereby granted, provided that the above copyright notice
// and this paragraph and the following two paragraphs appear in all copies.
//
// IN NO EVENT SHALL THE NPGSQL DEVELOPMENT TEAM BE LIABLE TO ANY PARTY
// FOR DIRECT, INDIRECT, SPECIAL, INCIDENTAL, OR CONSEQUENTIAL DAMAGES,
// INCLUDING LOST PROFITS, ARISING OUT OF THE USE OF THIS SOFTWARE AND ITS
// DOCUMENTATION, EVEN IF THE NPGSQL DEVELOPMENT TEAM HAS BEEN ADVISED OF
// THE POSSIBILITY OF SUCH DAMAGE.
//
// THE NPGSQL DEVELOPMENT TEAM SPECIFICALLY DISCLAIMS ANY WARRANTIES,
// INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY
// AND FITNESS FOR A PARTICULAR PURPOSE. THE SOFTWARE PROVIDED HEREUNDER IS
// ON AN "AS IS" BASIS, AND THE NPGSQL DEVELOPMENT TEAM HAS NO OBLIGATIONS
// TO PROVIDE MAINTENANCE, SUPPORT, UPDATES, ENHANCEMENTS, OR MODIFICATIONS.
#endregion

using System;
using System.Diagnostics;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Npgsql.BackendMessages;
using Npgsql.TypeHandling;
using Npgsql.TypeMapping;
using NpgsqlTypes;

namespace Npgsql.TypeHandlers
{
    /// <summary>
    /// Type Handler for the postgis geometry type.
    /// </summary>
    [TypeMapping("geometry", NpgsqlDbType.Geometry, new[]
    {
        typeof(PostgisGeometry),
        //2D Geometry
        typeof(PostgisPoint), typeof(PostgisMultiPoint), typeof(PostgisLineString),
        typeof(PostgisMultiLineString), typeof(PostgisPolygon), typeof(PostgisMultiPolygon),
        typeof(PostgisGeometryCollection),
        //3DZ Geometry
        typeof(PostgisPointZ), typeof(PostgisMultiPointZ), typeof(PostgisLineStringZ),
        typeof(PostgisMultiLineStringZ), typeof(PostgisPolygonZ), typeof(PostgisMultiPolygonZ),
        typeof(PostgisGeometryCollectionZ)
    })]
    class PostgisGeometryHandler : NpgsqlTypeHandler<PostgisGeometry>,
        //2D Geometry
        INpgsqlTypeHandler<PostgisPoint>, INpgsqlTypeHandler<PostgisMultiPoint>,
        INpgsqlTypeHandler<PostgisLineString>, INpgsqlTypeHandler<PostgisMultiLineString>,
        INpgsqlTypeHandler<PostgisPolygon>, INpgsqlTypeHandler<PostgisMultiPolygon>,
        INpgsqlTypeHandler<PostgisGeometryCollection>,
        //3DZ Geometry
        INpgsqlTypeHandler<PostgisPointZ>, INpgsqlTypeHandler<PostgisMultiPointZ>,
        INpgsqlTypeHandler<PostgisLineStringZ>, INpgsqlTypeHandler<PostgisMultiLineStringZ>,
        INpgsqlTypeHandler<PostgisPolygonZ>, INpgsqlTypeHandler<PostgisMultiPolygonZ>,
        INpgsqlTypeHandler<PostgisGeometryCollectionZ>,
        INpgsqlTypeHandler<byte[]>
    {
        [CanBeNull]
        readonly ByteaHandler _byteaHandler;

        public PostgisGeometryHandler()
        {
            _byteaHandler = new ByteaHandler();
        }

        #region Read

        public override async ValueTask<PostgisGeometry> Read(NpgsqlReadBuffer buf, int len, bool async, FieldDescription fieldDescription = null)
        {
            await buf.Ensure(5, async);
            var bo = (ByteOrder)buf.ReadByte();
            var id = buf.ReadUInt32(bo);

            var srid = 0u;
            if ((id & (uint)EwkbModifiers.HasSRID) != 0)
            {
                await buf.Ensure(4, async);
                srid = buf.ReadUInt32(bo);
            }

            PostgisGeometry geom;
            if ((id & (uint)EwkbModifiers.HasZDim) != 0 && (id & (uint)EwkbModifiers.HasMDim) != 0)
                geom = await DoReadXYZ(buf, (WkbIdentifier)((id & 7) + 3000), bo, async);
            else if ((id & (uint)EwkbModifiers.HasMDim) != 0)
                geom = await DoReadXYZ(buf, (WkbIdentifier)((id & 7) + 2000), bo, async);
            else if ((id & (uint)EwkbModifiers.HasZDim) != 0)
                geom = await DoReadXYZ(buf, (WkbIdentifier)((id & 7) + 1000), bo, async);
            else
                geom = await DoReadXY(buf, (WkbIdentifier)(id & 7), bo, async);

            geom.SRID = srid;
            return geom;
        }

        async ValueTask<PostgisGeometry<CoordinateXY>> DoReadXY(NpgsqlReadBuffer buf, WkbIdentifier id, ByteOrder bo, bool async)
        {
            switch (id)
            {
                case WkbIdentifier.Point:
                    await buf.Ensure(16, async);
                    return new PostgisPoint(buf.ReadDouble(bo), buf.ReadDouble(bo));

                case WkbIdentifier.LineString:
                    {
                        await buf.Ensure(4, async);
                        var points = new CoordinateXY[buf.ReadInt32(bo)];
                        for (var ipts = 0; ipts < points.Length; ipts++)
                        {
                            await buf.Ensure(16, async);
                            points[ipts] = new CoordinateXY(buf.ReadDouble(bo), buf.ReadDouble(bo));
                        }
                        return new PostgisLineString(points);
                    }

                case WkbIdentifier.Polygon:
                    {
                        await buf.Ensure(4, async);
                        var rings = new CoordinateXY[buf.ReadInt32(bo)][];

                        for (var irng = 0; irng < rings.Length; irng++)
                        {
                            await buf.Ensure(4, async);
                            rings[irng] = new CoordinateXY[buf.ReadInt32(bo)];
                            for (var ipts = 0; ipts < rings[irng].Length; ipts++)
                            {
                                await buf.Ensure(16, async);
                                rings[irng][ipts] = new CoordinateXY(buf.ReadDouble(bo), buf.ReadDouble(bo));
                            }
                        }
                        return new PostgisPolygon(rings);
                    }

                case WkbIdentifier.MultiPoint:
                    {
                        await buf.Ensure(4, async);
                        var points = new CoordinateXY[buf.ReadInt32(bo)];
                        for (var ipts = 0; ipts < points.Length; ipts++)
                        {
                            await buf.Ensure(21, async);
                            await buf.Skip(5, async);
                            points[ipts] = new CoordinateXY(buf.ReadDouble(bo), buf.ReadDouble(bo));
                        }
                        return new PostgisMultiPoint(points);
                    }

                case WkbIdentifier.MultiLineString:
                    {
                        await buf.Ensure(4, async);
                        var rings = new CoordinateXY[buf.ReadInt32(bo)][];

                        for (var irng = 0; irng < rings.Length; irng++)
                        {
                            await buf.Ensure(9, async);
                            await buf.Skip(5, async);
                            rings[irng] = new CoordinateXY[buf.ReadInt32(bo)];
                            for (var ipts = 0; ipts < rings[irng].Length; ipts++)
                            {
                                await buf.Ensure(16, async);
                                rings[irng][ipts] = new CoordinateXY(buf.ReadDouble(bo), buf.ReadDouble(bo));
                            }
                        }
                        return new PostgisMultiLineString(rings);
                    }

                case WkbIdentifier.MultiPolygon:
                    {
                        await buf.Ensure(4, async);
                        var pols = new CoordinateXY[buf.ReadInt32(bo)][][];

                        for (var ipol = 0; ipol < pols.Length; ipol++)
                        {
                            await buf.Ensure(9, async);
                            await buf.Skip(5, async);
                            pols[ipol] = new CoordinateXY[buf.ReadInt32(bo)][];
                            for (var irng = 0; irng < pols[ipol].Length; irng++)
                            {
                                await buf.Ensure(4, async);
                                pols[ipol][irng] = new CoordinateXY[buf.ReadInt32(bo)];
                                for (var ipts = 0; ipts < pols[ipol][irng].Length; ipts++)
                                {
                                    await buf.Ensure(16, async);
                                    pols[ipol][irng][ipts] = new CoordinateXY(buf.ReadDouble(bo), buf.ReadDouble(bo));
                                }
                            }
                        }
                        return new PostgisMultiPolygon(pols);
                    }

                case WkbIdentifier.GeometryCollection:
                    {
                        await buf.Ensure(4, async);
                        var g = new PostgisGeometry<CoordinateXY>[buf.ReadInt32(bo)];

                        for (var i = 0; i < g.Length; i++)
                        {
                            await buf.Ensure(5, async);
                            var elemBo = (ByteOrder)buf.ReadByte();
                            var elemId = (WkbIdentifier)(buf.ReadUInt32(bo) & 7);

                            g[i] = await DoReadXY(buf, elemId, elemBo, async);
                        }
                        return new PostgisGeometryCollection(g);
                    }

                default:
                    throw new InvalidOperationException("Unknown Postgis identifier.");
            }
        }

        async ValueTask<PostgisGeometry<CoordinateXYZ>> DoReadXYZ(NpgsqlReadBuffer buf, WkbIdentifier id, ByteOrder bo, bool async)
        {
            switch (id)
            {
                case WkbIdentifier.PointZ:
                    await buf.Ensure(24, async);
                    return new PostgisPointZ(buf.ReadDouble(bo), buf.ReadDouble(bo), buf.ReadDouble(bo));

                case WkbIdentifier.LineStringZ:
                    {
                        await buf.Ensure(4, async);
                        var points = new CoordinateXYZ[buf.ReadInt32(bo)];
                        for (var ipts = 0; ipts < points.Length; ipts++)
                        {
                            await buf.Ensure(16, async);
                            points[ipts] = new CoordinateXYZ(buf.ReadDouble(bo), buf.ReadDouble(bo), buf.ReadDouble(bo));
                        }
                        return new PostgisLineStringZ(points);
                    }

                case WkbIdentifier.PolygonZ:
                    {
                        await buf.Ensure(4, async);
                        var rings = new CoordinateXYZ[buf.ReadInt32(bo)][];

                        for (var irng = 0; irng < rings.Length; irng++)
                        {
                            await buf.Ensure(4, async);
                            rings[irng] = new CoordinateXYZ[buf.ReadInt32(bo)];
                            for (var ipts = 0; ipts < rings[irng].Length; ipts++)
                            {
                                await buf.Ensure(24, async);
                                rings[irng][ipts] = new CoordinateXYZ(buf.ReadDouble(bo), buf.ReadDouble(bo), buf.ReadDouble(bo));
                            }
                        }
                        return new PostgisPolygonZ(rings);
                    }

                case WkbIdentifier.MultiPointZ:
                    {
                        await buf.Ensure(4, async);
                        var points = new CoordinateXYZ[buf.ReadInt32(bo)];
                        for (var ipts = 0; ipts < points.Length; ipts++)
                        {
                            await buf.Ensure(21, async);
                            await buf.Skip(5, async);
                            points[ipts] = new CoordinateXYZ(buf.ReadDouble(bo), buf.ReadDouble(bo), buf.ReadDouble(bo));
                        }
                        return new PostgisMultiPointZ(points);
                    }

                case WkbIdentifier.MultiLineStringZ:
                    {
                        await buf.Ensure(4, async);
                        var rings = new CoordinateXYZ[buf.ReadInt32(bo)][];

                        for (var irng = 0; irng < rings.Length; irng++)
                        {
                            await buf.Ensure(9, async);
                            await buf.Skip(5, async);
                            rings[irng] = new CoordinateXYZ[buf.ReadInt32(bo)];
                            for (var ipts = 0; ipts < rings[irng].Length; ipts++)
                            {
                                await buf.Ensure(16, async);
                                rings[irng][ipts] = new CoordinateXYZ(buf.ReadDouble(bo), buf.ReadDouble(bo), buf.ReadDouble(bo));
                            }
                        }
                        return new PostgisMultiLineStringZ(rings);
                    }

                case WkbIdentifier.MultiPolygonZ:
                    {
                        await buf.Ensure(4, async);
                        var pols = new CoordinateXYZ[buf.ReadInt32(bo)][][];

                        for (var ipol = 0; ipol < pols.Length; ipol++)
                        {
                            await buf.Ensure(9, async);
                            await buf.Skip(5, async);
                            pols[ipol] = new CoordinateXYZ[buf.ReadInt32(bo)][];
                            for (var irng = 0; irng < pols[ipol].Length; irng++)
                            {
                                await buf.Ensure(4, async);
                                pols[ipol][irng] = new CoordinateXYZ[buf.ReadInt32(bo)];
                                for (var ipts = 0; ipts < pols[ipol][irng].Length; ipts++)
                                {
                                    await buf.Ensure(16, async);
                                    pols[ipol][irng][ipts] = new CoordinateXYZ(buf.ReadDouble(bo), buf.ReadDouble(bo), buf.ReadDouble(bo));
                                }
                            }
                        }
                        return new PostgisMultiPolygonZ(pols);
                    }

                case WkbIdentifier.GeometryCollectionZ:
                    {
                        await buf.Ensure(4, async);
                        var g = new PostgisGeometry<CoordinateXYZ>[buf.ReadInt32(bo)];

                        for (var i = 0; i < g.Length; i++)
                        {
                            await buf.Ensure(5, async);
                            var elemBo = (ByteOrder)buf.ReadByte();
                            var elemId = (WkbIdentifier)(buf.ReadUInt32(bo) & 7);

                            g[i] = await DoReadXYZ(buf, elemId, elemBo, async);
                        }
                        return new PostgisGeometryCollectionZ(g);
                    }

                default:
                    throw new InvalidOperationException("Unknown Postgis identifier.");
            }
        }

        #endregion Read

        #region Read concrete types

        //2D Geometry
        async ValueTask<PostgisPoint> INpgsqlTypeHandler<PostgisPoint>.Read(NpgsqlReadBuffer buf, int len, bool async, FieldDescription fieldDescription)
            => (PostgisPoint)await Read(buf, len, async, fieldDescription);
        async ValueTask<PostgisMultiPoint> INpgsqlTypeHandler<PostgisMultiPoint>.Read(NpgsqlReadBuffer buf, int len, bool async, FieldDescription fieldDescription)
            => (PostgisMultiPoint)await Read(buf, len, async, fieldDescription);
        async ValueTask<PostgisLineString> INpgsqlTypeHandler<PostgisLineString>.Read(NpgsqlReadBuffer buf, int len, bool async, FieldDescription fieldDescription)
            => (PostgisLineString)await Read(buf, len, async, fieldDescription);
        async ValueTask<PostgisMultiLineString> INpgsqlTypeHandler<PostgisMultiLineString>.Read(NpgsqlReadBuffer buf, int len, bool async, FieldDescription fieldDescription)
            => (PostgisMultiLineString)await Read(buf, len, async, fieldDescription);
        async ValueTask<PostgisPolygon> INpgsqlTypeHandler<PostgisPolygon>.Read(NpgsqlReadBuffer buf, int len, bool async, FieldDescription fieldDescription)
            => (PostgisPolygon)await Read(buf, len, async, fieldDescription);
        async ValueTask<PostgisMultiPolygon> INpgsqlTypeHandler<PostgisMultiPolygon>.Read(NpgsqlReadBuffer buf, int len, bool async, FieldDescription fieldDescription)
            => (PostgisMultiPolygon)await Read(buf, len, async, fieldDescription);
        async ValueTask<PostgisGeometryCollection> INpgsqlTypeHandler<PostgisGeometryCollection>.Read(NpgsqlReadBuffer buf, int len, bool async, FieldDescription fieldDescription)
            => (PostgisGeometryCollection)await Read(buf, len, async, fieldDescription);

        //3DZ Geometry
        async ValueTask<PostgisPointZ> INpgsqlTypeHandler<PostgisPointZ>.Read(NpgsqlReadBuffer buf, int len, bool async, FieldDescription fieldDescription)
            => (PostgisPointZ)await Read(buf, len, async, fieldDescription);
        async ValueTask<PostgisMultiPointZ> INpgsqlTypeHandler<PostgisMultiPointZ>.Read(NpgsqlReadBuffer buf, int len, bool async, FieldDescription fieldDescription)
            => (PostgisMultiPointZ)await Read(buf, len, async, fieldDescription);
        async ValueTask<PostgisLineStringZ> INpgsqlTypeHandler<PostgisLineStringZ>.Read(NpgsqlReadBuffer buf, int len, bool async, FieldDescription fieldDescription)
            => (PostgisLineStringZ)await Read(buf, len, async, fieldDescription);
        async ValueTask<PostgisMultiLineStringZ> INpgsqlTypeHandler<PostgisMultiLineStringZ>.Read(NpgsqlReadBuffer buf, int len, bool async, FieldDescription fieldDescription)
            => (PostgisMultiLineStringZ)await Read(buf, len, async, fieldDescription);
        async ValueTask<PostgisPolygonZ> INpgsqlTypeHandler<PostgisPolygonZ>.Read(NpgsqlReadBuffer buf, int len, bool async, FieldDescription fieldDescription)
            => (PostgisPolygonZ)await Read(buf, len, async, fieldDescription);
        async ValueTask<PostgisMultiPolygonZ> INpgsqlTypeHandler<PostgisMultiPolygonZ>.Read(NpgsqlReadBuffer buf, int len, bool async, FieldDescription fieldDescription)
            => (PostgisMultiPolygonZ)await Read(buf, len, async, fieldDescription);
        async ValueTask<PostgisGeometryCollectionZ> INpgsqlTypeHandler<PostgisGeometryCollectionZ>.Read(NpgsqlReadBuffer buf, int len, bool async, FieldDescription fieldDescription)
            => (PostgisGeometryCollectionZ)await Read(buf, len, async, fieldDescription);

        ValueTask<byte[]> INpgsqlTypeHandler<byte[]>.Read(NpgsqlReadBuffer buf, int len, bool async, FieldDescription fieldDescription)
        {
            Debug.Assert(_byteaHandler != null);
            return _byteaHandler.Read(buf, len, async, fieldDescription);
        }

        #endregion

        #region Write

        //2D Geometry
        public override int ValidateAndGetLength(PostgisGeometry value, ref NpgsqlLengthCache lengthCache, NpgsqlParameter parameter)
            => value.GetLen(true);

        public int ValidateAndGetLength(PostgisPoint value, ref NpgsqlLengthCache lengthCache, NpgsqlParameter parameter)
            => value.GetLen(true);

        public int ValidateAndGetLength(PostgisMultiPoint value, ref NpgsqlLengthCache lengthCache, NpgsqlParameter parameter)
            => value.GetLen(true);

        public int ValidateAndGetLength(PostgisPolygon value, ref NpgsqlLengthCache lengthCache, NpgsqlParameter parameter)
            => value.GetLen(true);

        public int ValidateAndGetLength(PostgisMultiPolygon value, ref NpgsqlLengthCache lengthCache, NpgsqlParameter parameter)
            => value.GetLen(true);

        public int ValidateAndGetLength(PostgisLineString value, ref NpgsqlLengthCache lengthCache, NpgsqlParameter parameter)
            => value.GetLen(true);

        public int ValidateAndGetLength(PostgisMultiLineString value, ref NpgsqlLengthCache lengthCache, NpgsqlParameter parameter)
            => value.GetLen(true);

        public int ValidateAndGetLength(PostgisGeometryCollection value, ref NpgsqlLengthCache lengthCache, NpgsqlParameter parameter)
            => value.GetLen(true);

        //3DZ Geometry
        public int ValidateAndGetLength(PostgisPointZ value, ref NpgsqlLengthCache lengthCache, NpgsqlParameter parameter)
            => value.GetLen(true);

        public int ValidateAndGetLength(PostgisMultiPointZ value, ref NpgsqlLengthCache lengthCache, NpgsqlParameter parameter)
            => value.GetLen(true);

        public int ValidateAndGetLength(PostgisLineStringZ value, ref NpgsqlLengthCache lengthCache, NpgsqlParameter parameter)
            => value.GetLen(true);

        public int ValidateAndGetLength(PostgisMultiLineStringZ value, ref NpgsqlLengthCache lengthCache, NpgsqlParameter parameter)
            => value.GetLen(true);

        public int ValidateAndGetLength(PostgisPolygonZ value, ref NpgsqlLengthCache lengthCache, NpgsqlParameter parameter)
            => value.GetLen(true);

        public int ValidateAndGetLength(PostgisMultiPolygonZ value, ref NpgsqlLengthCache lengthCache, NpgsqlParameter parameter)
            => value.GetLen(true);

        public int ValidateAndGetLength(PostgisGeometryCollectionZ value, ref NpgsqlLengthCache lengthCache, NpgsqlParameter parameter)
            => value.GetLen(true);

        public int ValidateAndGetLength(byte[] value, ref NpgsqlLengthCache lengthCache, NpgsqlParameter parameter)
            => value.Length;

        public override async Task Write(PostgisGeometry value, NpgsqlWriteBuffer buf, NpgsqlLengthCache lengthCache, NpgsqlParameter parameter, bool async)
        {
            // Common header
            if (value.SRID == 0)
            {
                if (buf.WriteSpaceLeft < 5)
                    await buf.Flush(async);
                buf.WriteByte(0); // We choose to ouput only XDR structure
                buf.WriteInt32((int)value.Identifier);
            }
            else
            {
                if (buf.WriteSpaceLeft < 9)
                    await buf.Flush(async);
                buf.WriteByte(0);
                buf.WriteInt32((int)((uint)value.Identifier | (uint)EwkbModifiers.HasSRID));
                buf.WriteInt32((int)value.SRID);
            }

            switch (value.Identifier)
            {
                case WkbIdentifier.Point:
                    if (buf.WriteSpaceLeft < 16)
                        await buf.Flush(async);
                    var p = (PostgisPoint)value;
                    buf.WriteDouble(p.X);
                    buf.WriteDouble(p.Y);
                    return;

                case WkbIdentifier.LineString:
                    var l = (PostgisLineString)value;
                    if (buf.WriteSpaceLeft < 4)
                        await buf.Flush(async);
                    buf.WriteInt32(l.PointCount);
                    for (var ipts = 0; ipts < l.PointCount; ipts++)
                    {
                        if (buf.WriteSpaceLeft < 16)
                            await buf.Flush(async);
                        buf.WriteDouble(l[ipts].X);
                        buf.WriteDouble(l[ipts].Y);
                    }
                    return;

                case WkbIdentifier.Polygon:
                    var pol = (PostgisPolygon)value;
                    if (buf.WriteSpaceLeft < 4)
                        await buf.Flush(async);
                    buf.WriteInt32(pol.RingCount);
                    for (var irng = 0; irng < pol.RingCount; irng++)
                    {
                        if (buf.WriteSpaceLeft < 4)
                            await buf.Flush(async);
                        buf.WriteInt32(pol[irng].Length);
                        for (var ipts = 0; ipts < pol[irng].Length; ipts++)
                        {
                            if (buf.WriteSpaceLeft < 16)
                                await buf.Flush(async);
                            buf.WriteDouble(pol[irng][ipts].X);
                            buf.WriteDouble(pol[irng][ipts].Y);
                        }
                    }
                    return;

                case WkbIdentifier.MultiPoint:
                    var mp = (PostgisMultiPoint)value;
                    if (buf.WriteSpaceLeft < 4)
                        await buf.Flush(async);
                    buf.WriteInt32(mp.PointCount);
                    for (var ipts = 0; ipts < mp.PointCount; ipts++)
                    {
                        if (buf.WriteSpaceLeft < 21)
                            await buf.Flush(async);
                        buf.WriteByte(0);
                        buf.WriteInt32((int)WkbIdentifier.Point);
                        buf.WriteDouble(mp[ipts].X);
                        buf.WriteDouble(mp[ipts].Y);
                    }
                    return;

                case WkbIdentifier.MultiLineString:
                    var ml = (PostgisMultiLineString)value;
                    if (buf.WriteSpaceLeft < 4)
                        await buf.Flush(async);
                    buf.WriteInt32(ml.LineCount);
                    for (var irng = 0; irng < ml.LineCount; irng++)
                    {
                        if (buf.WriteSpaceLeft < 9)
                            await buf.Flush(async);
                        buf.WriteByte(0);
                        buf.WriteInt32((int)WkbIdentifier.LineString);
                        buf.WriteInt32(ml[irng].PointCount);
                        for (var ipts = 0; ipts < ml[irng].PointCount; ipts++)
                        {
                            if (buf.WriteSpaceLeft < 16)
                                await buf.Flush(async);
                            buf.WriteDouble(ml[irng][ipts].X);
                            buf.WriteDouble(ml[irng][ipts].Y);
                        }
                    }
                    return;

                case WkbIdentifier.MultiPolygon:
                    var mpl = (PostgisMultiPolygon)value;
                    if (buf.WriteSpaceLeft < 4)
                        await buf.Flush(async);
                    buf.WriteInt32(mpl.PolygonCount);
                    for (var ipol = 0; ipol < mpl.PolygonCount; ipol++)
                    {
                        if (buf.WriteSpaceLeft < 9)
                            await buf.Flush(async);
                        buf.WriteByte(0);
                        buf.WriteInt32((int)WkbIdentifier.Polygon);
                        buf.WriteInt32(mpl[ipol].RingCount);
                        for (var irng = 0; irng < mpl[ipol].RingCount; irng++)
                        {
                            if (buf.WriteSpaceLeft < 4)
                                await buf.Flush(async);
                            buf.WriteInt32(mpl[ipol][irng].Length);
                            for (var ipts = 0; ipts < mpl[ipol][irng].Length; ipts++)
                            {
                                if (buf.WriteSpaceLeft < 16)
                                    await buf.Flush(async);
                                buf.WriteDouble(mpl[ipol][irng][ipts].X);
                                buf.WriteDouble(mpl[ipol][irng][ipts].Y);
                            }
                        }
                    }
                    return;

                case WkbIdentifier.GeometryCollection:
                    var coll = (PostgisGeometryCollection)value;
                    if (buf.WriteSpaceLeft < 4)
                        await buf.Flush(async);
                    buf.WriteInt32(coll.GeometryCount);

                    foreach (var x in coll)
                        await Write(x, buf, lengthCache, null, async);
                    return;

                case WkbIdentifier.PointZ:
                    if (buf.WriteSpaceLeft < 24)
                        await buf.Flush(async);
                    var pz = (PostgisPointZ)value;
                    buf.WriteDouble(pz.X);
                    buf.WriteDouble(pz.Y);
                    buf.WriteDouble(pz.Z);
                    return;

                case WkbIdentifier.LineStringZ:
                    var lz = (PostgisLineStringZ)value;
                    if (buf.WriteSpaceLeft < 4)
                        await buf.Flush(async);
                    buf.WriteInt32(lz.PointCount);
                    for (var ipts = 0; ipts < lz.PointCount; ipts++)
                    {
                        if (buf.WriteSpaceLeft < 24)
                            await buf.Flush(async);
                        buf.WriteDouble(lz[ipts].X);
                        buf.WriteDouble(lz[ipts].Y);
                        buf.WriteDouble(lz[ipts].Z);
                    }
                    return;

                case WkbIdentifier.PolygonZ:
                    var polz = (PostgisPolygonZ)value;
                    if (buf.WriteSpaceLeft < 4)
                        await buf.Flush(async);
                    buf.WriteInt32(polz.RingCount);
                    for (var irng = 0; irng < polz.RingCount; irng++)
                    {
                        if (buf.WriteSpaceLeft < 4)
                            await buf.Flush(async);
                        buf.WriteInt32(polz[irng].Length);
                        for (var ipts = 0; ipts < polz[irng].Length; ipts++)
                        {
                            if (buf.WriteSpaceLeft < 24)
                                await buf.Flush(async);
                            buf.WriteDouble(polz[irng][ipts].X);
                            buf.WriteDouble(polz[irng][ipts].Y);
                            buf.WriteDouble(polz[irng][ipts].Z);
                        }
                    }
                    return;

                case WkbIdentifier.MultiPointZ:
                    var mpz = (PostgisMultiPointZ)value;
                    if (buf.WriteSpaceLeft < 4)
                        await buf.Flush(async);
                    buf.WriteInt32(mpz.PointCount);
                    for (var ipts = 0; ipts < mpz.PointCount; ipts++)
                    {
                        if (buf.WriteSpaceLeft < 29)
                            await buf.Flush(async);
                        buf.WriteByte(0);
                        buf.WriteInt32((int)WkbIdentifier.PointZ);
                        buf.WriteDouble(mpz[ipts].X);
                        buf.WriteDouble(mpz[ipts].Y);
                        buf.WriteDouble(mpz[ipts].Z);
                    }
                    return;

                case WkbIdentifier.MultiLineStringZ:
                    var mlz = (PostgisMultiLineStringZ)value;
                    if (buf.WriteSpaceLeft < 4)
                        await buf.Flush(async);
                    buf.WriteInt32(mlz.LineCount);
                    for (var irng = 0; irng < mlz.LineCount; irng++)
                    {
                        if (buf.WriteSpaceLeft < 9)
                            await buf.Flush(async);
                        buf.WriteByte(0);
                        buf.WriteInt32((int)WkbIdentifier.LineStringZ);
                        buf.WriteInt32(mlz[irng].PointCount);
                        for (var ipts = 0; ipts < mlz[irng].PointCount; ipts++)
                        {
                            if (buf.WriteSpaceLeft < 24)
                                await buf.Flush(async);
                            buf.WriteDouble(mlz[irng][ipts].X);
                            buf.WriteDouble(mlz[irng][ipts].Y);
                            buf.WriteDouble(mlz[irng][ipts].Z);
                        }
                    }
                    return;

                case WkbIdentifier.MultiPolygonZ:
                    var mplz = (PostgisMultiPolygonZ)value;
                    if (buf.WriteSpaceLeft < 4)
                        await buf.Flush(async);
                    buf.WriteInt32(mplz.PolygonCount);
                    for (var ipol = 0; ipol < mplz.PolygonCount; ipol++)
                    {
                        if (buf.WriteSpaceLeft < 9)
                            await buf.Flush(async);
                        buf.WriteByte(0);
                        buf.WriteInt32((int)WkbIdentifier.PolygonZ);
                        buf.WriteInt32(mplz[ipol].RingCount);
                        for (var irng = 0; irng < mplz[ipol].RingCount; irng++)
                        {
                            if (buf.WriteSpaceLeft < 4)
                                await buf.Flush(async);
                            buf.WriteInt32(mplz[ipol][irng].Length);
                            for (var ipts = 0; ipts < mplz[ipol][irng].Length; ipts++)
                            {
                                if (buf.WriteSpaceLeft < 24)
                                    await buf.Flush(async);
                                buf.WriteDouble(mplz[ipol][irng][ipts].X);
                                buf.WriteDouble(mplz[ipol][irng][ipts].Y);
                                buf.WriteDouble(mplz[ipol][irng][ipts].Z);
                            }
                        }
                    }
                    return;

                case WkbIdentifier.GeometryCollectionZ:
                    var collz = (PostgisGeometryCollectionZ)value;
                    if (buf.WriteSpaceLeft < 4)
                        await buf.Flush(async);
                    buf.WriteInt32(collz.GeometryCount);

                    foreach (var x in collz)
                        await Write(x, buf, lengthCache, null, async);
                    return;

                default:
                    throw new InvalidOperationException("Unknown Postgis identifier.");
            }
        }

        //2D Geometry
        public Task Write(PostgisPoint value, NpgsqlWriteBuffer buf, NpgsqlLengthCache lengthCache, NpgsqlParameter parameter, bool async)
            => Write((PostgisGeometry<CoordinateXY>)value, buf, lengthCache, parameter, async);

        public Task Write(PostgisMultiPoint value, NpgsqlWriteBuffer buf, NpgsqlLengthCache lengthCache, NpgsqlParameter parameter, bool async)
            => Write((PostgisGeometry<CoordinateXY>)value, buf, lengthCache, parameter, async);

        public Task Write(PostgisPolygon value, NpgsqlWriteBuffer buf, NpgsqlLengthCache lengthCache, NpgsqlParameter parameter, bool async)
            => Write((PostgisGeometry<CoordinateXY>)value, buf, lengthCache, parameter, async);

        public Task Write(PostgisMultiPolygon value, NpgsqlWriteBuffer buf, NpgsqlLengthCache lengthCache, NpgsqlParameter parameter, bool async)
            => Write((PostgisGeometry<CoordinateXY>)value, buf, lengthCache, parameter, async);

        public Task Write(PostgisLineString value, NpgsqlWriteBuffer buf, NpgsqlLengthCache lengthCache, NpgsqlParameter parameter, bool async)
            => Write((PostgisGeometry<CoordinateXY>)value, buf, lengthCache, parameter, async);

        public Task Write(PostgisMultiLineString value, NpgsqlWriteBuffer buf, NpgsqlLengthCache lengthCache, NpgsqlParameter parameter, bool async)
            => Write((PostgisGeometry<CoordinateXY>)value, buf, lengthCache, parameter, async);

        public Task Write(PostgisGeometryCollection value, NpgsqlWriteBuffer buf, NpgsqlLengthCache lengthCache, NpgsqlParameter parameter, bool async)
            => Write((PostgisGeometry<CoordinateXY>)value, buf, lengthCache, parameter, async);

        //3DZ Geometry
        public Task Write(PostgisPointZ value, NpgsqlWriteBuffer buf, NpgsqlLengthCache lengthCache, NpgsqlParameter parameter, bool async)
            => Write((PostgisGeometry<CoordinateXYZ>)value, buf, lengthCache, parameter, async);

        public Task Write(PostgisMultiPointZ value, NpgsqlWriteBuffer buf, NpgsqlLengthCache lengthCache, NpgsqlParameter parameter, bool async)
            => Write((PostgisGeometry<CoordinateXYZ>)value, buf, lengthCache, parameter, async);

        public Task Write(PostgisLineStringZ value, NpgsqlWriteBuffer buf, NpgsqlLengthCache lengthCache, NpgsqlParameter parameter, bool async)
            => Write((PostgisGeometry<CoordinateXYZ>)value, buf, lengthCache, parameter, async);

        public Task Write(PostgisMultiLineStringZ value, NpgsqlWriteBuffer buf, NpgsqlLengthCache lengthCache, NpgsqlParameter parameter, bool async)
            => Write((PostgisGeometry<CoordinateXYZ>)value, buf, lengthCache, parameter, async);

        public Task Write(PostgisPolygonZ value, NpgsqlWriteBuffer buf, NpgsqlLengthCache lengthCache, NpgsqlParameter parameter, bool async)
            => Write((PostgisGeometry<CoordinateXYZ>)value, buf, lengthCache, parameter, async);

        public Task Write(PostgisMultiPolygonZ value, NpgsqlWriteBuffer buf, NpgsqlLengthCache lengthCache, NpgsqlParameter parameter, bool async)
            => Write((PostgisGeometry<CoordinateXYZ>)value, buf, lengthCache, parameter, async);

        public Task Write(PostgisGeometryCollectionZ value, NpgsqlWriteBuffer buf, NpgsqlLengthCache lengthCache, NpgsqlParameter parameter, bool async)
            => Write((PostgisGeometry<CoordinateXYZ>)value, buf, lengthCache, parameter, async);

        public Task Write(byte[] value, NpgsqlWriteBuffer buf, NpgsqlLengthCache lengthCache, NpgsqlParameter parameter, bool async)
            => _byteaHandler == null
                ? throw new NpgsqlException("Bytea handler was not found during initialization of PostGIS handler")
                : _byteaHandler.Write(value, buf, lengthCache, parameter, async);

        #endregion Write
    }
}
