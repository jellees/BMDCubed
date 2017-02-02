﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenTK;
using grendgine_collada;

namespace BMDCubed.src.BMD
{
    class Bone
    {
        // What these do are currently not known
        public byte Unknown1;
        public byte Unknown2;
        public byte Unknown3;
        public byte Unknown4;

        public Vector3 Scale;
        public Vector3 Translation;
        public Vector3 Rotation;
        public BoundingBox Bounds;

        public string Name;

        public Matrix3x4 InverseBindMatrix;

        public List<Bone> Children;

        public Bone(Grendgine_Collada_Node node)
        {
            Children = new List<Bone>();

            Name = node.Name;

            float[] nodeTransform = node.Matrix[0].Value();
            Matrix4 transform = new Matrix4(nodeTransform[0], nodeTransform[1], nodeTransform[2], nodeTransform[3],
                                            nodeTransform[4], nodeTransform[5], nodeTransform[6], nodeTransform[7],
                                            nodeTransform[8], nodeTransform[9], nodeTransform[10], nodeTransform[11],
                                            nodeTransform[12], nodeTransform[13], nodeTransform[14], nodeTransform[15]);

            Scale = transform.ExtractScale();
            Translation = new Vector3(transform.Column3[0], transform.Column3[1], transform.Column3[2]);
            Rotation = transform.ExtractRotation().Xyz;

            if (node.node == null)
                return;

            foreach (Grendgine_Collada_Node childNode in node.node)
            {
                Bone child = new Bone(childNode);
                Children.Add(child);
            }
        }
    }
}
