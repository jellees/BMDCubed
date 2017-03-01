﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using grendgine_collada;
using OpenTK;
using GameFormatReader.Common;
using BMDCubed.src.BMD.Skinning;

namespace BMDCubed.src.BMD.Geometry
{
    struct Triangle
    {
        public List<int>[] VertexIndexes;
        public List<int> MatrixList;
        public List<Weight> Weights;
        public List<Weight> UniqueWeights;

        public void SetVertexData(List<int> data, int index)
        {
            if (VertexIndexes == null)
                VertexIndexes = new List<int>[3];

            VertexIndexes[index] = data;
        }

        public void AddVertexWeight(Weight wight)
        {
            if (Weights == null)
                Weights = new List<Weight>();
            if (UniqueWeights == null)
                UniqueWeights = new List<Weight>();

            Weights.Add(wight);

            if (!UniqueWeights.Contains(wight))
                UniqueWeights.Add(wight);

            if (MatrixList == null)
                MatrixList = new List<int>();

            for (int i = 0; i < UniqueWeights.Count; i++)
            {
                for (int b = 0; b < UniqueWeights[i].BoneIndexes.Count; b++)
                {
                    if (!MatrixList.Contains(UniqueWeights[i].BoneIndexes[b]))
                        MatrixList.Add(UniqueWeights[i].BoneIndexes[b]);
                }
            }
        }

        public void SwapFirstLastVertex()
        {
            List<int> temp1 = VertexIndexes[0];
            VertexIndexes[0] = VertexIndexes[2];
            VertexIndexes[2] = temp1;

            Weight tempWeight = Weights[0];
            Weights[0] = Weights[2];
            Weights[2] = tempWeight;
        }
    }

    class Batch
    {
        public List<VertexAttributes> ActiveAttributes;
        public Dictionary<VertexAttributes, List<short>> AttributeData;
        public List<Packet> Packets;
        public int AttributeIndex = 0;

        List<short> VertIndexes;
        List<int> PositionIndex;
        public List<int> WeightIndexes;

        int numTris;
        int numVerts;

        public string MaterialName;
        BoundingBox Bounds;

        public Batch(Grendgine_Collada_Triangles tri, DrawData drw1)
        {
            if (tri.Count == 0)
                return;

            ActiveAttributes = new List<VertexAttributes>();
            AttributeData = new Dictionary<VertexAttributes, List<short>>();

            Packets = new List<Packet>();
            VertIndexes = new List<short>();
            PositionIndex = new List<int>();
            WeightIndexes = new List<int>();
            MaterialName = tri.Material;
            numTris = tri.Count;

            // This will parse the vertex attributes in the batch.
            int uvIndex = 0;
            int colorIndex = 0;
            foreach (Grendgine_Collada_Input_Shared input in tri.Input)
            {
                switch (input.Semantic)
                {
                    case Grendgine_Collada_Input_Semantic.VERTEX:
                    case Grendgine_Collada_Input_Semantic.POSITION:
                        ActiveAttributes.Add(VertexAttributes.Position);
                        break;
                    case Grendgine_Collada_Input_Semantic.NORMAL:
                        ActiveAttributes.Add(VertexAttributes.Normal);
                        break;
                    case Grendgine_Collada_Input_Semantic.COLOR:
                        ActiveAttributes.Add(VertexAttributes.Color0 + colorIndex++);
                        break;
                    case Grendgine_Collada_Input_Semantic.TEXCOORD:
                        ActiveAttributes.Add(VertexAttributes.Tex0 + uvIndex++);
                        break;
                    default:
                        throw new FormatException(string.Format("Found unknown DAE semantic {0}!", input.Semantic));
                }
            }

            numVerts = numTris * 3;

            // Grab the index data from the DAE's string array
            string indexArrayString = tri.P.Value_As_String;
            indexArrayString = indexArrayString.Replace('\n', ' ').Trim();
            int[] indexArray = Grendgine_Collada_Parse_Utils.String_To_Int(indexArrayString);

            if (drw1 != null)
                GetVertexDataWeighted_New(indexArray, drw1);
            else
                GetVertexDataNotWeighted(indexArray);
        }

        private void GetVertexDataWeighted(int[] indexArray, DrawData drw1)
        {
            // Load the data into VertIndexes. This version will add MatrixPositionIndex to the
            // list of attributes for weighted meshes.
            for (int i = 0; i < indexArray.Length; i += ActiveAttributes.Count)
            {
                int matrixPosIndex = 0;

                for (int attrib = 0; attrib < ActiveAttributes.Count; attrib++)
                {
                    if (ActiveAttributes[attrib] == VertexAttributes.Position)
                    {
                        int positionIndex = indexArray[i + attrib];
                        PositionIndex.Add(positionIndex);

                        if (!WeightIndexes.Contains(drw1.AllDrw1Weights.IndexOf(drw1.AllWeights[positionIndex])))
                        {
                            WeightIndexes.Add(drw1.AllDrw1Weights.IndexOf(drw1.AllWeights[positionIndex]));
                            matrixPosIndex = (WeightIndexes.Count - 1) * 3;
                        }
                        else
                            matrixPosIndex = WeightIndexes.IndexOf(drw1.AllDrw1Weights.IndexOf(drw1.AllWeights[positionIndex])) * 3;

                        VertIndexes.Add((short)((matrixPosIndex)));
                        VertIndexes.Add((short)positionIndex);
                    }
                    else
                    {
                        VertIndexes.Add((short)indexArray[i + attrib]);
                    }
                }
            }

            // Now that we're done parsing the data that was in the file, we can
            // add Position Matrix Index to the start of the attribute data.
            // This is the index used to start the chain that gives the
            // vertexes skinning data.
            ActiveAttributes.Insert(0, VertexAttributes.PositionMatrixIndex);

            foreach (VertexAttributes attrib in ActiveAttributes)
                AttributeData.Add(attrib, new List<short>());

            // The triangles from the DAE have the wrong winding order. We need to swap the first
            // and last vertexes of each triangle to flip them around.
            // If we don't do that, the mesh will render inside-out!
            // We add 3 * ActiveAttributes.Count so that we can get the correct indexes of each
            // vertex triplet.
            for (int i = 0; i < VertIndexes.Count; i += 3 * ActiveAttributes.Count)
            {
                SwapVertexes(i, i + (2 * ActiveAttributes.Count));
            }

            // We'll separate the indexes by attribute type. This will allow us to 
            // sort the attributes in ActiveAttributes independently of the indexes'
            // order. With that, we can give GX the attribute indexes in the order
            // that it expects.
            int runningIndex = 0;
            for (int i = 0; i < numVerts; i++)
            {
                foreach (VertexAttributes attrib in ActiveAttributes)
                {
                    AttributeData[attrib].Add(VertIndexes[runningIndex++]);
                }
            }

            ActiveAttributes.Sort();
        }

        private void GetVertexDataWeighted_New(int[] indexArray, DrawData drw1)
        {
            // We need to divide up the indexes into triangles, and swap the first and last vertexes.
            // Then, we will divide up the triangles into packets based on their position matrix indexes.

            List<Triangle> AllTris = new List<Triangle>(); // Master list of all triangles

            int currentIndex = 0; // This is a running index which we'll use to access the indexArray, which contains the data for every vertex
            
            // For each triangle...
            for (int i = 0; i < numTris; i++)
            {
                Triangle tri = new Triangle();
                
                // For each vertex of the triangle...
                for (int b = 0; b < 3; b++)
                {
                    List<int> indexes = new List<int>(); // This will hold the attribute indexes for this vertex
                    int positionMatrixIndex = 0;

                    // For each vertex attribute in the vertex...
                    foreach (VertexAttributes attrib in ActiveAttributes)
                    {
                        // If this is the position index, add the corresponding weight to the triangle.
                        if (attrib == VertexAttributes.Position)
                        {
                            tri.AddVertexWeight(drw1.AllWeights[indexArray[currentIndex]]);
                            PositionIndex.Add(indexArray[currentIndex]);

                            Weight weight = drw1.AllWeights[indexArray[currentIndex]];
                            positionMatrixIndex = drw1.AllDrw1Weights.IndexOf(weight);
                        }

                        // Add the index for this attribute to the list for this vertex
                        indexes.Add(indexArray[currentIndex++]);
                    }

                    indexes.Add(positionMatrixIndex);

                    tri.SetVertexData(indexes, b);
                }

                tri.SwapFirstLastVertex();
                AllTris.Add(tri);
            }

            ActiveAttributes.Add(VertexAttributes.PositionMatrixIndex);

            // Now we'll run through the triangles in the list we just made and put them into packets.
            Packet currentPacket = new Packet(ActiveAttributes);

            for (int i = 0; i < numTris; i++)
            {
                Triangle curTri = AllTris[i];

                if (currentPacket.CanAddTriToPacket(curTri))
                    currentPacket.AddTriVertexes(curTri);
                else
                {
                    Packets.Add(currentPacket);
                    currentPacket = new Packet(ActiveAttributes);
                    i--;
                }
            }

            Packets.Add(currentPacket);

            ActiveAttributes.Sort();
        }

        private void GetVertexDataNotWeighted(int[] indexArray)
        {
            // This will get the vertex indexes into VertIndexes.
            // This version does not use the PositionMatrixIndex attribute.
            for (int i = 0; i < indexArray.Length; i += ActiveAttributes.Count)
            {
                for (int attrib = 0; attrib < ActiveAttributes.Count; attrib++)
                {
                    if (ActiveAttributes[attrib] == VertexAttributes.Position)
                    {
                        int positionIndex = indexArray[i + attrib];
                        PositionIndex.Add(positionIndex);
                    }

                    VertIndexes.Add((short)indexArray[i + attrib]);
                }
            }

            foreach (VertexAttributes attrib in ActiveAttributes)
                AttributeData.Add(attrib, new List<short>());

            // The triangles from the DAE have the wrong winding order. We need to swap the first
            // and last vertexes of each triangle to flip them around.
            // If we don't do that, the mesh will render inside-out!
            // We add 3 * ActiveAttributes.Count so that we can get the correct indexes of each
            // vertex triplet.
            for (int i = 0; i < VertIndexes.Count; i += 3 * ActiveAttributes.Count)
            {
                SwapVertexes(i, i + (2 * ActiveAttributes.Count));
            }

            // We'll separate the indexes by attribute type. This will allow us to 
            // sort the attributes in ActiveAttributes independently of the indexes'
            // order. With that, we can give GX the attribute indexes in the order
            // that it expects.
            int runningIndex = 0;
            for (int i = 0; i < numVerts; i++)
            {
                foreach (VertexAttributes attrib in ActiveAttributes)
                {
                    AttributeData[attrib].Add(VertIndexes[runningIndex++]);
                }
            }

            ActiveAttributes.Sort();

            WeightIndexes.Add(0);
        }

        private void SwapVertexes(int vert1, int vert2)
        {
            int[] vertData1 = new int[ActiveAttributes.Count]; // Data for vertex 1
            int[] vertData2 = new int[ActiveAttributes.Count]; // Data for vertex 2

            for (int i = 0; i < ActiveAttributes.Count; i++)
            {
                // Store data from the two vertexes
                vertData1[i] = VertIndexes[vert1 + i];
                vertData2[i] = VertIndexes[vert2 + i];
            }

            for (int i = 0; i < ActiveAttributes.Count; i++)
            {
                // Swap the two vertexes index-by-index
                VertIndexes[vert1 + i] = (short)vertData2[i];
                VertIndexes[vert2 + i] = (short)vertData1[i];
            }
        }

        public void GetBoundingBoxData(List<Vector3> posList)
        {
            List<Vector3> listForBounds = new List<Vector3>();

            foreach (int inte in PositionIndex)
            {
                listForBounds.Add(posList[inte]);
            }

            Bounds = new BoundingBox(listForBounds);
        }

        public void WriteBatch(EndianBinaryWriter writer, List<int> attributeOffsets, int packetDataIndex)
        {
            writer.Write((byte)3); // Write matrix type. 0 is basic, 1 is ???, 2 is ???, 3 is multi-matrix.
            writer.Write((byte)0xFF); // Write padding
            writer.Write((short)Packets.Count);

            writer.Write((short)(attributeOffsets[AttributeIndex])); // Write the offset to the attributes in this batch.
            writer.Write((short)packetDataIndex); // This is the index of the first matrix index entry belonging to this batch.
            writer.Write((short)packetDataIndex); // This is the index of the first packet info entry beloning to this batch.

            writer.Write((short)-1); // Padding

            Bounds.WriteBoundingBox(writer); // Write bounding box.
        }

        public void WriteMatrixIndexes(EndianBinaryWriter writer)
        {
            if (Packets.Count == 0)
                WriteMatrixIndexesNoSkinning(writer);
            else
                WriteMatrixIndexesSkinning(writer);
        }

        private void WriteMatrixIndexesNoSkinning(EndianBinaryWriter writer)
        {
            for (int i = 0; i < WeightIndexes.Count; i++)
                writer.Write((ushort)WeightIndexes[i]);
        }

        private void WriteMatrixIndexesSkinning(EndianBinaryWriter writer)
        {
            for (int i = 0; i < Packets.Count; i++)
            {
                Packets[i].WriteMatrixIndexes(writer);
            }
        }

        public void WritePacket(EndianBinaryWriter writer)
        {
            writer.Write((byte)0x90); // Write primitive type. For the foreseeable future, we will only support triangles, which are 0x90.
            writer.Write((ushort)numVerts); // Vertex count

            // For each vertex, we're going to run through the vertex attributes
            // and write the corresponding data.
            for (int i = 0; i < numVerts; i++)
            {
                foreach (VertexAttributes attrib in ActiveAttributes)
                {
                    if (attrib == VertexAttributes.PositionMatrixIndex)
                        writer.Write((byte)(AttributeData[attrib][i])); // PositionMatrixIndex needs to be 8 bits, so it's a byte
                    else
                        writer.Write(AttributeData[attrib][i]); // All other attributes will use 16 bit short
                }
            }

            // We pad to the nearest 32 byte boundary.
            // We need to pad with zeroes here because that
            // signals GX that there is no more packet data to be read.
            Util.PadStreamWithZero(writer, 32);
        }
    }
}
