using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace tinybvh
{
    public class BVH
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public struct Intersection
        {
            public uint inst;
            public float t;
            public float u;
            public float v;
            public uint prim;
            public IntPtr auxData;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 56)]
            public byte[] userData; // Represents the 56-byte union
        }

        [DllImport("unity-tinybvh-plugin", CallingConvention = CallingConvention.Cdecl)]
        private static extern int BuildBVH(IntPtr verticesPtr, int startTri, int triCount, bool buildCWBVH);

        [DllImport("unity-tinybvh-plugin", CallingConvention = CallingConvention.Cdecl)]
        private static extern void DestroyBVH(int index);

        [DllImport("unity-tinybvh-plugin", CallingConvention = CallingConvention.Cdecl)]
        private static extern bool IsBVHReady(int index);

        [DllImport("unity-tinybvh-plugin", CallingConvention = CallingConvention.Cdecl)]
        private static extern Intersection Intersect(int index, Vector3 origin, Vector3 direction, bool useCWBVH);
        
        [DllImport("unity-tinybvh-plugin", CallingConvention = CallingConvention.Cdecl)]
        private static extern int GetCWBVHNodesSize(int index);
        
        [DllImport("unity-tinybvh-plugin", CallingConvention = CallingConvention.Cdecl)]
        private static extern int GetCWBVHTrisSize(int index);

        [DllImport("unity-tinybvh-plugin", CallingConvention = CallingConvention.Cdecl)]
        private static extern bool GetCWBVHData(int index, out IntPtr bvhNodes, out IntPtr bvhTris);

        [DllImport("unity-tinybvh-plugin", CallingConvention = CallingConvention.Cdecl)]
        public static extern void UpdateTransform(int index, Matrix4x4 transform);

        // Index of the BVH internal to the plugin.
        private int index = -1;

        // Construct a new BVH from the given vertices.
        // Set buildCWBVH to true if this is intended for GPU traversal.
        public void Build(IntPtr verticesPtr, int startTri, int triCount, bool buildCWBVH = false)
        {
            if (index >= 0)
            {
                Destroy();
            }

            index = BuildBVH(verticesPtr, startTri, triCount, buildCWBVH);
        }

        // Frees the memory of the BVH in the plugin.
        public void Destroy()
        {
            if (index < 0) return;
            DestroyBVH(index);
            index = -1;
        }

        // Returns true if the BVH has finished building and can be read or intersected with.
        public bool IsReady()
        {
            if (index < 0) return false;
            return IsBVHReady(index);
        }

        // Perform CPU ray intersection
        public Intersection Intersect(Vector3 origin, Vector3 direction, bool useCWBVH = false)
        {
            if (index < 0) return new Intersection();
            return Intersect(index, origin, direction, useCWBVH);
        }

        // Retrieve the size of the CWBVH nodes array in bytes.
        public int GetCWBVHNodesSize()
        {
            if (index < 0) return 0;
            return GetCWBVHNodesSize(index);
        }

        // Retrieve the size of the CWBVH triangles array in bytes.
        public int GetCWBVHTrisSize()
        {
            if (index < 0) return 0;
            return GetCWBVHTrisSize(index);
        }

        // Retrieve the CWBVH data for GPU upload.
        public bool GetCWBVHData(out IntPtr bvhNodes, out IntPtr bvhTris)
        {
            if (index < 0)
            {
                bvhNodes = IntPtr.Zero;
                bvhTris = IntPtr.Zero;
                return false;
            }

            return GetCWBVHData(index, out bvhNodes, out bvhTris);
        }

        public void UpdateTransform(Matrix4x4 transform)
        {
            if (index < 0) return;
            UpdateTransform(index, transform);
        }
    }
}