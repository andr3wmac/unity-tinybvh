using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;

using tinybvh;

public class BVHScene : MonoBehaviour
{
    private MeshRenderer[] meshRenderers;
    private ComputeShader meshProcessingShader;
    private LocalKeyword has32BitIndicesKeyword;
    private LocalKeyword hasNormalsKeyword;
    private LocalKeyword hasUVsKeyword;

    private int totalVertexCount   = 0;
    private int totalTriangleCount = 0;
    private DateTime readbackStartTime;

    private ComputeBuffer vertexPositionBufferGPU;
    private NativeArray<Vector4> vertexPositionBufferCPU;
    private ComputeBuffer triangleAttributesBuffer;

    // TLAS and BLAS data
    private ComputeBuffer bvhNodeBuffer;
    private ComputeBuffer bvhTriBuffer;
    private ComputeBuffer tlasNodeBuffer;
    private ComputeBuffer tlasIndexBuffer;
    private ComputeBuffer blasInstanceBuffer;

    // Struct sizes in bytes
    private const int VertexPositionSize    = 16;
    private const int TriangleAttributeSize = 60;
    private const int BVHNodeSize           = 80;
    private const int BVHTriSize            = 16;
    private const int TLASNodeSize          = 64;
    private const int TLASIndexSize         = 4;
    private const int BLASInstanceSize      = 140;

    public class BVHMesh
    {
        public MeshRenderer meshRenderer;
        
        public BVH bvh;
        public int triOffset;
        public int triCount;
    }
    private List<BVHMesh> bvhMeshes = new List<BVHMesh>();

    struct BLASInstance
    {
        public Matrix4x4 transform;
        public Matrix4x4 invTransform;
        public uint bvhNodeOffset;
        public uint bvhTriOffset;
        public uint triOffset;
    }
    private BLASInstance[] blasInstances;

    void Start()
    {
        // Load compute shader
        meshProcessingShader   = Resources.Load<ComputeShader>("MeshProcessing");
        has32BitIndicesKeyword = meshProcessingShader.keywordSpace.FindKeyword("HAS_32_BIT_INDICES");
        hasNormalsKeyword      = meshProcessingShader.keywordSpace.FindKeyword("HAS_NORMALS");
        hasUVsKeyword          = meshProcessingShader.keywordSpace.FindKeyword("HAS_UVS");

        // Populate list of mesh renderers to trace against
        meshRenderers = FindObjectsOfType<MeshRenderer>();

        ProcessMeshes();
    }

    void OnDestroy()
    {
        vertexPositionBufferGPU?.Release();
        triangleAttributesBuffer?.Release();

        bvhNodeBuffer?.Release();
        bvhTriBuffer?.Release();
        tlasNodeBuffer?.Release();
        tlasIndexBuffer?.Release();
        blasInstanceBuffer?.Release();

        if (vertexPositionBufferCPU.IsCreated)
        {
            vertexPositionBufferCPU.Dispose();
        }

        foreach (BVHMesh mesh in bvhMeshes)
        {
            if (mesh.bvh != null)
            {
                mesh.bvh.Destroy();
            }
        }

        TLAS.DestroyTLAS();
    }

    private void ProcessMeshes()
    {
        totalVertexCount   = 0;
        totalTriangleCount = 0;
        bvhMeshes.Clear();

        // Gather info on the meshes we'll be using
        foreach (MeshRenderer renderer in meshRenderers)
        {
            Mesh mesh = renderer.gameObject.GetComponent<MeshFilter>().sharedMesh;
            if (mesh == null)
            {
                continue;
            }

            totalVertexCount += Utilities.GetTriangleCount(mesh) * 3;
        }

        // Allocate buffers
        vertexPositionBufferGPU  = new ComputeBuffer(totalVertexCount, VertexPositionSize);
        vertexPositionBufferCPU  = new NativeArray<Vector4>(totalVertexCount * VertexPositionSize, Allocator.Persistent);
        triangleAttributesBuffer = new ComputeBuffer(totalVertexCount / 3, TriangleAttributeSize);

        // Pack each mesh into global vertex buffer via compute shader
        // Note: this avoids the need for every mesh to have cpu read/write access.
        foreach (MeshRenderer renderer in meshRenderers)
        {
            Mesh mesh = renderer.gameObject.GetComponent<MeshFilter>().sharedMesh;
            if (mesh == null)
            {
                continue;
            }

            mesh.vertexBufferTarget |= GraphicsBuffer.Target.Raw;
            GraphicsBuffer vertexBuffer = mesh.GetVertexBuffer(0);

            mesh.indexBufferTarget |= GraphicsBuffer.Target.Raw;
            GraphicsBuffer indexBuffer = mesh.GetIndexBuffer();
            
            int triangleCount = Utilities.GetTriangleCount(mesh);

            // Determine where in the Unity vertex buffer each vertex attribute is
            int vertexStride, positionOffset, normalOffset, uvOffset;
            Utilities.FindVertexAttribute(mesh, VertexAttribute.Position, out positionOffset, out vertexStride);
            Utilities.FindVertexAttribute(mesh, VertexAttribute.Normal, out normalOffset, out vertexStride);
            Utilities.FindVertexAttribute(mesh, VertexAttribute.TexCoord0, out uvOffset, out vertexStride);

            meshProcessingShader.SetBuffer(0, "VertexBuffer", vertexBuffer);
            meshProcessingShader.SetBuffer(0, "IndexBuffer", indexBuffer);
            meshProcessingShader.SetBuffer(0, "VertexPositionBuffer", vertexPositionBufferGPU);
            meshProcessingShader.SetBuffer(0, "TriangleAttributesBuffer", triangleAttributesBuffer);
            meshProcessingShader.SetInt("VertexStride", vertexStride);
            meshProcessingShader.SetInt("PositionOffset", positionOffset);
            meshProcessingShader.SetInt("NormalOffset", normalOffset);
            meshProcessingShader.SetInt("UVOffset", uvOffset);
            meshProcessingShader.SetInt("TriangleCount", triangleCount);
            meshProcessingShader.SetInt("OutputTriangleStart", totalTriangleCount);

            // Set keywords based on format/attributes of this mesh
            meshProcessingShader.SetKeyword(has32BitIndicesKeyword, (mesh.indexFormat == IndexFormat.UInt32));
            meshProcessingShader.SetKeyword(hasNormalsKeyword, mesh.HasVertexAttribute(VertexAttribute.Normal));
            meshProcessingShader.SetKeyword(hasUVsKeyword, mesh.HasVertexAttribute(VertexAttribute.TexCoord0));

            meshProcessingShader.Dispatch(0, Mathf.CeilToInt(triangleCount / 64.0f), 1, 1);

            BVHMesh bvhMesh = new BVHMesh();
            bvhMesh.meshRenderer = renderer;
            bvhMesh.bvh          = new BVH();
            bvhMesh.triOffset    = totalTriangleCount;
            bvhMesh.triCount     = triangleCount;
            bvhMeshes.Add(bvhMesh);

            totalTriangleCount += triangleCount;
        }

        Debug.Log("Meshes processed. Total triangles: " + totalTriangleCount);

        // Initiate async readback of vertex buffer to pass to tinybvh to build
        readbackStartTime = DateTime.UtcNow;
        AsyncGPUReadback.RequestIntoNativeArray(ref vertexPositionBufferCPU, vertexPositionBufferGPU, OnCompleteReadback);
    }

    public BVHMesh GetMesh(int index)
    {
        if (index >= bvhMeshes.Count)
        {
            return null;
        }

        return bvhMeshes[index];
    }

    private unsafe void OnCompleteReadback(AsyncGPUReadbackRequest request)
    {
        if (request.hasError)
        {
            Debug.LogError("GPU readback error.");
            return;
        }

        TimeSpan readbackTime = DateTime.UtcNow - readbackStartTime;
        Debug.Log("GPU readback took: " + readbackTime.TotalMilliseconds + "ms");

        // In the editor if we exit play mode before the bvh is finished building the memory will be freed
        // and tinybvh will illegal access and crash everything. 
        #if UNITY_EDITOR
            NativeArray<Vector4> persistentBuffer = new NativeArray<Vector4>(vertexPositionBufferCPU.Length, Allocator.Persistent);
            persistentBuffer.CopyFrom(vertexPositionBufferCPU);
            var dataPointer = (IntPtr)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(persistentBuffer);
        #else
            var dataPointer = (IntPtr)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(vertexPositionBufferCPU);
        #endif
        
        // Build BVHs in thread.
        // Note: Should build each BVH on separate threads.
        Thread thread = new Thread(() =>
        {
            DateTime bvhStartTime = DateTime.UtcNow;

            foreach (BVHMesh mesh in bvhMeshes)
            {
                mesh.bvh.Build(dataPointer, mesh.triOffset, mesh.triCount, true);
            }

            TimeSpan bvhTime = DateTime.UtcNow - bvhStartTime;

            Debug.Log(bvhMeshes.Count + " BVH(s) built in: " + bvhTime.TotalMilliseconds + "ms");

            #if UNITY_EDITOR
                persistentBuffer.Dispose();
            #endif
        });

        thread.Start();
    }

    private void Update()
    {
        // Ensure all the BLAS BVHs are prepared.
        if (!PrepareBVHBuffers())
        {
            return;
        }

        // Update transforms
        for (int i = 0; i < bvhMeshes.Count; ++i)
        {
            BVHMesh mesh = bvhMeshes[i];
            Matrix4x4 localToWorld = mesh.meshRenderer.localToWorldMatrix;

            BLASInstance instance = blasInstances[i];
            instance.transform    = localToWorld;
            instance.invTransform = localToWorld.inverse;
            blasInstances[i] = instance;

            mesh.bvh.UpdateTransform(localToWorld.transpose);
        }

        Utilities.PrepareBuffer(ref blasInstanceBuffer, blasInstances.Length, BLASInstanceSize);
        blasInstanceBuffer.SetData(blasInstances);

        // Rebuild TLAS
        if (TLAS.BuildTLAS())
        {
            int nodesSize   = TLAS.GetTLASNodesSize();
            int indicesSize = TLAS.GetTLASIndicesSize();

            IntPtr nodesPtr, indicesPtr;
            if (TLAS.GetTLASData(out nodesPtr, out indicesPtr))
            {
                Utilities.PrepareBuffer(ref tlasNodeBuffer, nodesSize / TLASNodeSize, TLASNodeSize);
                Utilities.PrepareBuffer(ref tlasIndexBuffer, indicesSize / TLASIndexSize, TLASIndexSize);
                Utilities.UploadFromPointer(ref tlasNodeBuffer, nodesPtr, nodesSize, TLASNodeSize);
                Utilities.UploadFromPointer(ref tlasIndexBuffer, indicesPtr, indicesSize, TLASIndexSize);
            } 
            else
            {
                Debug.LogError("Failed to fetch updated TLAS data.");
            }
        } 
        else 
        {
            Debug.LogError("Failed to build TLAS.");
        }
    }

    private bool PrepareBVHBuffers()
    {
        int totalNodeCount = 0;
        int totalTriCount  = 0;

        foreach (BVHMesh mesh in bvhMeshes)
        {
            if (mesh.bvh.IsReady())
            {
                totalNodeCount += mesh.bvh.GetCWBVHNodesSize() / BVHNodeSize;
                totalTriCount += mesh.bvh.GetCWBVHTrisSize() / BVHTriSize;
            } 
            else 
            {
                // Exit until all meshes are ready.
                return false;
            }
        }

        Utilities.PrepareBuffer(ref bvhNodeBuffer, totalNodeCount, BVHNodeSize, ComputeBufferMode.SubUpdates);
        Utilities.PrepareBuffer(ref bvhTriBuffer, totalTriCount, BVHTriSize, ComputeBufferMode.SubUpdates);
        Utilities.PrepareArray(ref blasInstances, bvhMeshes.Count);

        int dstNode = 0;
        int dstTri  = 0;
        for (int i = 0; i < bvhMeshes.Count; ++i)
        {
            BVHMesh mesh = bvhMeshes[i];
            int nodesSize = mesh.bvh.GetCWBVHNodesSize();
            int trisSize  = mesh.bvh.GetCWBVHTrisSize();

            IntPtr nodesPtr, trisPtr;
            if (mesh.bvh.GetCWBVHData(out nodesPtr, out trisPtr))
            {
                Utilities.UploadFromPointer(ref bvhNodeBuffer, nodesPtr, nodesSize, BVHNodeSize, dstNode);
                Utilities.UploadFromPointer(ref bvhTriBuffer, trisPtr, trisSize, BVHTriSize, dstTri);
            } 
            else
            {
                Debug.LogError("Failed to fetch updated BVH data.");
            }

            BLASInstance blasInstance = new BLASInstance();
            blasInstance.invTransform   = mesh.meshRenderer.transform.worldToLocalMatrix;
            blasInstance.bvhNodeOffset  = (uint)dstNode;
            blasInstance.bvhTriOffset   = (uint)dstTri;
            blasInstance.triOffset      = (uint)mesh.triOffset;
            blasInstances[i] = blasInstance;

            dstNode += nodesSize / BVHNodeSize;
            dstTri += trisSize / BVHTriSize;
        }

        return true;
    }

    public bool CanRender()
    {
        return tlasNodeBuffer != null && tlasIndexBuffer != null && blasInstanceBuffer != null &&
               bvhNodeBuffer != null && bvhTriBuffer != null && triangleAttributesBuffer != null;
    }

    public void PrepareShader(CommandBuffer cmd, ComputeShader shader, int kernelIndex)
    {
        if (!CanRender())
        {
            return;
        }

        cmd.SetComputeBufferParam(shader, kernelIndex, "TLASNodes", tlasNodeBuffer);
        cmd.SetComputeBufferParam(shader, kernelIndex, "TLASIndices", tlasIndexBuffer);
        cmd.SetComputeBufferParam(shader, kernelIndex, "BLASInstances", blasInstanceBuffer);
        cmd.SetComputeBufferParam(shader, kernelIndex, "BVHNodes", bvhNodeBuffer);
        cmd.SetComputeBufferParam(shader, kernelIndex, "BVHTris", bvhTriBuffer);
        cmd.SetComputeBufferParam(shader, kernelIndex, "TriangleAttributesBuffer", triangleAttributesBuffer);
    }

    public BVH.Intersection Intersect(Vector3 origin, Vector3 direction, bool useCWBVH = false)
    {
        return TLAS.IntersectTLAS(origin, direction);
    }
}
