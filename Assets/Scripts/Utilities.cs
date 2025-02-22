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

    // Finds the offset of an attribute as well as the vertex stride.
    public static void FindVertexAttribute(Mesh mesh, VertexAttribute targetAttribute, out int attributeOffset, out int vertexStride)
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

    // Returns the total number of triangles in a given mesh.
    public static int GetTriangleCount(Mesh mesh)
    {
        int result = 0;
        for (int i = 0; i < mesh.subMeshCount; ++i)
        {
            result += (int)mesh.GetIndexCount(i) / 3;
        }
        return result;
    }

    // Ensures the buffer is created and matches the requested count and stride.
    public static void PrepareBuffer(ref ComputeBuffer buffer, int count, int stride, ComputeBufferMode mode = ComputeBufferMode.Immutable)
    {
        if (buffer != null && (buffer.count != count || buffer.stride != stride))
        {
            buffer.Release();
            buffer = null;
        }

        if (buffer == null)
        {
            buffer = new ComputeBuffer(count, stride, ComputeBufferType.Structured, mode);
        }
    }

    // Ensures the render texture is created and matches the requested width, height, and format.
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
        NativeArray<float> srcArray = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<float>(
            dataPtr.ToPointer(),
            dataSize / 4,
            Allocator.None
        );

        #if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle atomicSafetyHandle = AtomicSafetyHandle.Create();
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref srcArray, atomicSafetyHandle);
        #endif

        PrepareBuffer(ref buffer, dataSize / dataStride, dataStride);
        buffer.SetData(srcArray);

        #if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckDeallocateAndThrow(atomicSafetyHandle);
            AtomicSafetyHandle.Release(atomicSafetyHandle);
        #endif
    }

    public unsafe static void UploadFromPointer(ref ComputeBuffer buffer, IntPtr dataPtr, int dataSize, int dataStride, int dstOffset)
    {
        int elementCount = dataSize / 4;

        NativeArray<float> srcArray = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<float>(
            dataPtr.ToPointer(),
            elementCount,
            Allocator.None
        );

        #if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle atomicSafetyHandle = AtomicSafetyHandle.Create();
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref srcArray, atomicSafetyHandle);
        #endif

        NativeArray<float> dstArray = buffer.BeginWrite<float>(dstOffset / 4, elementCount);
        //NativeArray<float>.Copy(nativeArray, 0, gpuArray, dstOffset, elementCount);
        srcArray.CopyTo(dstArray);
        buffer.EndWrite<float>(elementCount);

        #if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckDeallocateAndThrow(atomicSafetyHandle);
            AtomicSafetyHandle.Release(atomicSafetyHandle);
        #endif
    }
}