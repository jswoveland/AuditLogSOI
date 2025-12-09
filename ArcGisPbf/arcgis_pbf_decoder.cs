/***
 * ArcGisPbf - A C# library for decoding ArcGIS PBF (Protocol Buffer) feature collections.
 * This code was converted to C# by Claude AI from the /rowanwins/arcgis-pbf-parser 
 * javascript library, at https://github.com/rowanwins/arcgis-pbf-parser.
 * 
 * Use at your own risk. No warranties or guarantees.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using Google.Protobuf;
using EsriPBuffer; // This namespace comes from your .proto file

namespace ArcGisPbf
{
    public class FeatureCollectionDecoder
    {
        public DecodedResult Decode(byte[] featureCollectionBuffer)
        {
            FeatureCollectionPBuffer decodedObject;
            try
            {
                decodedObject = FeatureCollectionPBuffer.Parser.ParseFrom(featureCollectionBuffer);
            }
            catch (Exception error)
            {
                throw new Exception("Could not parse arcgis-pbf buffer", error);
            }

            var featureResult = decodedObject.QueryResult.FeatureResult;
            var transform = featureResult.Transform;
            var geometryType = (int)featureResult.GeometryType;
            var objectIdField = featureResult.ObjectIdFieldName;

            var featureCollection = new FeatureCollection
            {
                Type = "FeatureCollection",
                Features = new List<Feature>()
            };

            var geometryParser = GetGeometryParser(geometryType);

            foreach (var f in featureResult.Features)
            {
                featureCollection.Features.Add(new Feature
                {
                    Type = "Feature",
                    Id = GetFeatureId(featureResult.Fields, f.Attributes, objectIdField),
                    Properties = CollectAttributes(featureResult.Fields, f.Attributes),
                    Geometry = f.Geometry != null ? geometryParser(f, transform) : null
                });
            }

            return new DecodedResult
            {
                FeatureCollection = featureCollection,
                ExceededTransferLimit = featureResult.ExceededTransferLimit
            };
        }

        private Func<EsriPBuffer.FeatureCollectionPBuffer.Types.Feature,
                     EsriPBuffer.FeatureCollectionPBuffer.Types.Transform,
                     Geometry> GetGeometryParser(int featureType)
        {
            return featureType switch
            {
                3 => CreatePolygon,
                2 => CreateLine,
                0 => CreatePoint,
                _ => CreatePolygon
            };
        }

        private Geometry CreatePoint(
            EsriPBuffer.FeatureCollectionPBuffer.Types.Feature f,
            EsriPBuffer.FeatureCollectionPBuffer.Types.Transform transform)
        {
            return new Geometry
            {
                Type = "Point",
                Coordinates = TransformTuple(
                    f.Geometry.Coords.ToArray(),
                    transform)
            };
        }

        private Geometry CreateLine(
            EsriPBuffer.FeatureCollectionPBuffer.Types.Feature f,
            EsriPBuffer.FeatureCollectionPBuffer.Types.Transform transform)
        {
            var lengths = f.Geometry.Lengths.Count;

            if (lengths == 1)
            {
                return new Geometry
                {
                    Type = "LineString",
                    Coordinates = CreateLinearRing(
                        f.Geometry.Coords.ToList(),
                        transform,
                        0,
                        (int)(f.Geometry.Lengths[0] * 2))
                };
            }
            else if (lengths > 1)
            {
                var coordinates = new List<List<double[]>>();
                var startPoint = 0;

                for (int index = 0; index < lengths; index++)
                {
                    var stopPoint = startPoint + (f.Geometry.Lengths[index] * 2);
                    var line = CreateLinearRing(
                        f.Geometry.Coords.ToList(),
                        transform,
                        startPoint,
                        (int)stopPoint);
                    coordinates.Add(line);
                    startPoint = (int)stopPoint;
                }

                return new Geometry
                {
                    Type = "MultiLineString",
                    Coordinates = coordinates
                };
            }

            return null;
        }

        private Geometry CreatePolygon(
            EsriPBuffer.FeatureCollectionPBuffer.Types.Feature f,
            EsriPBuffer.FeatureCollectionPBuffer.Types.Transform transform)
        {
            var lengths = f.Geometry.Lengths.Count;

            // Always use List<List<double[]>> for Coordinates
            var coordinates = new List<List<double[]>>();

            if (lengths == 1)
            {
                coordinates.Add(CreateLinearRing(
                    f.Geometry.Coords.ToList(),
                    transform,
                    0,
                    (int)(f.Geometry.Lengths[0] * 2)));
                return new Geometry
                {
                    Type = "Polygon",
                    Coordinates = coordinates
                };
            }
            else
            {
                var startPoint = 0;

                for (int index = 0; index < lengths; index++)
                {
                    var stopPoint = startPoint + (f.Geometry.Lengths[index] * 2);
                    var ring = CreateLinearRing(
                        f.Geometry.Coords.ToList(),
                        transform,
                        startPoint,
                        (int)stopPoint);

                    if (RingIsClockwise(ring))
                    {
                        // Start a new polygon
                        coordinates.Add(ring);
                    }
                    else if (coordinates.Count > 0)
                    {
                        // Add as a hole to the last polygon
                        coordinates[coordinates.Count - 1].AddRange(ring);
                    }

                    startPoint = (int)stopPoint;
                }

                return new Geometry
                {
                    Type = "MultiPolygon",
                    Coordinates = coordinates
                };
            }
        }

        private bool RingIsClockwise(List<double[]> ringToTest)
        {
            double total = 0;
            var rLength = ringToTest.Count;
            var pt1 = ringToTest[0];

            for (int i = 0; i < rLength - 1; i++)
            {
                var pt2 = ringToTest[i + 1];
                total += (pt2[0] - pt1[0]) * (pt2[1] + pt1[1]);
                pt1 = pt2;
            }

            return total >= 0;
        }

        private List<double[]> CreateLinearRing(
            List<long> arr,
            EsriPBuffer.FeatureCollectionPBuffer.Types.Transform transform,
            int startPoint,
            int stopPoint)
        {
            var output = new List<double[]>();
            if (arr.Count == 0) return output;

            var initialX = arr[startPoint];
            var initialY = arr[startPoint + 1];
            output.Add(TransformTuple(new[] { initialX, initialY }, transform));

            var prevX = initialX;
            var prevY = initialY;

            for (int i = startPoint + 2; i < stopPoint; i += 2)
            {
                var x = Difference(prevX, arr[i]);
                var y = Difference(prevY, arr[i + 1]);
                var transformed = TransformTuple(new[] { x, y }, transform);
                output.Add(transformed);
                prevX = x;
                prevY = y;
            }

            return output;
        }

        private Dictionary<string, object> CollectAttributes(
            Google.Protobuf.Collections.RepeatedField<EsriPBuffer.FeatureCollectionPBuffer.Types.Field> fields,
            Google.Protobuf.Collections.RepeatedField<EsriPBuffer.FeatureCollectionPBuffer.Types.Value> featureAttributes)
        {
            var output = new Dictionary<string, object>();

            for (int i = 0; i < fields.Count; i++)
            {
                var field = fields[i];
                var attr = featureAttributes[i];
                var value = GetValueFromProtobuf(attr);
                output[field.Name] = value;
            }

            return output;
        }

        private object GetValueFromProtobuf(EsriPBuffer.FeatureCollectionPBuffer.Types.Value value)
        {
            return value.ValueTypeCase switch
            {
                EsriPBuffer.FeatureCollectionPBuffer.Types.Value.ValueTypeOneofCase.StringValue => value.StringValue,
                EsriPBuffer.FeatureCollectionPBuffer.Types.Value.ValueTypeOneofCase.FloatValue => value.FloatValue,
                EsriPBuffer.FeatureCollectionPBuffer.Types.Value.ValueTypeOneofCase.DoubleValue => value.DoubleValue,
                EsriPBuffer.FeatureCollectionPBuffer.Types.Value.ValueTypeOneofCase.SintValue => value.SintValue,
                EsriPBuffer.FeatureCollectionPBuffer.Types.Value.ValueTypeOneofCase.UintValue => value.UintValue,
                EsriPBuffer.FeatureCollectionPBuffer.Types.Value.ValueTypeOneofCase.Int64Value => value.Int64Value,
                EsriPBuffer.FeatureCollectionPBuffer.Types.Value.ValueTypeOneofCase.Uint64Value => value.Uint64Value,
                EsriPBuffer.FeatureCollectionPBuffer.Types.Value.ValueTypeOneofCase.Sint64Value => value.Sint64Value,
                EsriPBuffer.FeatureCollectionPBuffer.Types.Value.ValueTypeOneofCase.BoolValue => value.BoolValue,
                _ => null
            };
        }

        private object GetFeatureId(
            Google.Protobuf.Collections.RepeatedField<EsriPBuffer.FeatureCollectionPBuffer.Types.Field> fields,
            Google.Protobuf.Collections.RepeatedField<EsriPBuffer.FeatureCollectionPBuffer.Types.Value> featureAttributes,
            string featureIdField)
        {
            for (int index = 0; index < fields.Count; index++)
            {
                var field = fields[index];
                if (field.Name == featureIdField)
                {
                    return GetValueFromProtobuf(featureAttributes[index]);
                }
            }
            return null;
        }

        private double[] TransformTuple(
            long[] coords,
            EsriPBuffer.FeatureCollectionPBuffer.Types.Transform transform)
        {
            double x = coords[0];
            double y = coords[1];
            double? z = coords.Length > 2 ? coords[2] : null;

            if (transform.Scale != null)
            {
                x *= transform.Scale.XScale;
                y *= -transform.Scale.YScale;
                if (z.HasValue)
                {
                    z = z.Value * transform.Scale.ZScale;
                }
            }

            if (transform.Translate != null)
            {
                x += transform.Translate.XTranslate;
                y += transform.Translate.YTranslate;
                if (z.HasValue)
                {
                    z = z.Value + transform.Translate.ZTranslate;
                }
            }

            return z.HasValue ? new[] { x, y, z.Value } : new[] { x, y };
        }

        private long Difference(long a, long b)
        {
            return a + b;
        }
    }

    // Output classes remain the same
    public class DecodedResult
    {
        public FeatureCollection FeatureCollection { get; set; }
        public bool ExceededTransferLimit { get; set; }
    }

    public class FeatureCollection
    {
        public string Type { get; set; }
        public List<Feature> Features { get; set; }
    }

    public class Feature
    {
        public string Type { get; set; }
        public object Id { get; set; }
        public Dictionary<string, object> Properties { get; set; }
        public Geometry Geometry { get; set; }
    }

    public class Geometry
    {
        public string Type { get; set; }
        public object Coordinates { get; set; }
    }
}