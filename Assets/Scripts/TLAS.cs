using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace tinybvh
{
    public class TLAS
    {
        [DllImport("unity-tinybvh-plugin", CallingConvention = CallingConvention.Cdecl)]
        public static extern bool BuildTLAS();

        [DllImport("unity-tinybvh-plugin", CallingConvention = CallingConvention.Cdecl)]
        public static extern bool DestroyTLAS();
        
        [DllImport("unity-tinybvh-plugin", CallingConvention = CallingConvention.Cdecl)]
        public static extern int GetTLASNodesSize();
        
        [DllImport("unity-tinybvh-plugin", CallingConvention = CallingConvention.Cdecl)]
        public static extern int GetTLASIndicesSize();

        [DllImport("unity-tinybvh-plugin", CallingConvention = CallingConvention.Cdecl)]
        public static extern bool GetTLASData(out IntPtr bvhNodes, out IntPtr bvhIndices);

        [DllImport("unity-tinybvh-plugin", CallingConvention = CallingConvention.Cdecl)]
        public static extern BVH.Intersection IntersectTLAS(Vector3 origin, Vector3 direction);
    }
}