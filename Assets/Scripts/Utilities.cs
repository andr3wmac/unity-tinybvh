using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;

public static class Utilities
{
    public struct DebugRay
    {
        public Vector3 origin;
        public Vector3 direction;
    };

    public struct DebugRayHit
    {
        public float t;
        public Vector2 barycentric;
        public uint triIndex;
        public uint steps;
    };

    public static void FindVertexAttribute(Mesh mesh, VertexAttribute targetAttribute, out int vertexStride, out int attributeOffset)
    {
        VertexAttributeDescriptor[] vertexAttributes = mesh.GetVertexAttributes();
        attributeOffset = 0;
        vertexStride = 0;

        foreach (var attribute in vertexAttributes)
        {
            if (attribute.attribute == targetAttribute)
            {
                attributeOffset = vertexStride;
            }

            // Increment vertexStride by the size of the current attribute
            switch (attribute.format)
            {
                case VertexAttributeFormat.Float32:
                case VertexAttributeFormat.UInt32:
                case VertexAttributeFormat.SInt32:
                    vertexStride += 4 * attribute.dimension;
                    break;
                case VertexAttributeFormat.Float16:
                case VertexAttributeFormat.UNorm16:
                case VertexAttributeFormat.SNorm16:
                    vertexStride += 2 * attribute.dimension;
                    break;
                case VertexAttributeFormat.UNorm8:
                case VertexAttributeFormat.SNorm8:
                case VertexAttributeFormat.UInt8:
                case VertexAttributeFormat.SInt8:
                    vertexStride += 1 * attribute.dimension;
                    break;
                default:
                    Debug.LogError("Unsupported vertex format");
                    break;
            }
        }
    }

    public static int GetTriangleCount(Mesh mesh)
    {
        int result = 0;
        for (int i = 0; i < mesh.subMeshCount; ++i)
        {
            result += (int)mesh.GetIndexCount(i) / 3;
        }
        return result;
    }

    public static void PrepareBuffer(ref ComputeBuffer buffer, int count, int stride)
    {
        // Check if the buffer matches the requested count and stride
        if (buffer != null && (buffer.count != count || buffer.stride != stride))
        {
            buffer.Release();
            buffer = null;
        }

        if (buffer == null)
        {
            buffer = new ComputeBuffer(count, stride, ComputeBufferType.Structured);
        }
    }

    public static void PrepareRenderTexture(ref RenderTexture texture, int width, int height, RenderTextureFormat format)
    {
        // Check if the texture matches the requested dimensions and format
        if (texture != null && (texture.width != width || texture.height != height || texture.format != format))
        {
            texture.Release();
            texture = null;
        }

        if (texture == null)
        {
            texture = new RenderTexture(width, height, 0, format, RenderTextureReadWrite.Linear);
            texture.enableRandomWrite = true;
            texture.Create();
        }
    }

    // Populates a compute buffer from a native data pointer without copying the data into managed memory
    public unsafe static void UploadFromPointer(ref ComputeBuffer buffer, IntPtr dataPtr, int dataSize, int dataStride)
    {
        NativeArray<float> nativeArray = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<float>(
            dataPtr.ToPointer(),
            dataSize / 4,
            Allocator.None
        );

        #if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle atomicSafetyHandle = AtomicSafetyHandle.Create();
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref nativeArray, atomicSafetyHandle);
        #endif

        PrepareBuffer(ref buffer, dataSize / dataStride, dataStride);
        buffer.SetData(nativeArray);

        #if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckDeallocateAndThrow(atomicSafetyHandle);
            AtomicSafetyHandle.Release(atomicSafetyHandle);
        #endif
    }
}