using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(Camera))]
public class Raytracer : MonoBehaviour
{
    public enum DisplayMode
    {
        NDotL,
        RayDistance,
        BVHSteps,
        Barycentrics,
        Normals,
        UV,
    }

    public DisplayMode display = DisplayMode.NDotL;

    private Camera sourceCamera;
    private BVHScene bvhScene;
    private CommandBuffer cmd;

    private ComputeShader rayGenerationShader;
    private ComputeShader rayIntersectionShader;
    private ComputeShader rayShadingShader;

    private int outputWidth;
    private int outputHeight;
    private int totalRays;
    private RenderTexture outputRT;
    private ComputeBuffer rayBuffer;
    private ComputeBuffer rayHitBuffer;

    // Shading options
    private LocalKeyword outputNDotL;
    private LocalKeyword outputBVHSteps;
    private LocalKeyword outputBarycentrics;
    private LocalKeyword outputNormals;
    private LocalKeyword outputUVs;

    // Sun for NDotL
    private Vector3 lightDirection = new Vector3(1.0f, 1.0f, 1.0f).normalized;
    private Vector4 lightColor = new Vector4(1.0f, 1.0f, 1.0f, 1.0f);

    // Struct sizes in bytes
    private const int RayStructSize = 24;
    private const int RayHitStructSize = 24;

    void Start()
    {
        sourceCamera = GetComponent<Camera>();
        bvhScene = FindObjectOfType<BVHScene>();
        cmd = new CommandBuffer();

        if (bvhScene == null)
        {
            Debug.LogError("BVHManager was not found in the scene!");
        }

        rayGenerationShader   = Resources.Load<ComputeShader>("RayGeneration");
        rayIntersectionShader = Resources.Load<ComputeShader>("RayIntersection");
        rayShadingShader      = Resources.Load<ComputeShader>("RayShading");

        outputNDotL         = rayShadingShader.keywordSpace.FindKeyword("OUTPUT_NDOTL");
        outputBVHSteps      = rayShadingShader.keywordSpace.FindKeyword("OUTPUT_BVH_STEPS");
        outputBarycentrics  = rayShadingShader.keywordSpace.FindKeyword("OUTPUT_BARYCENTRICS");
        outputNormals       = rayShadingShader.keywordSpace.FindKeyword("OUTPUT_NORMALS");
        outputUVs           = rayShadingShader.keywordSpace.FindKeyword("OUTPUT_UVS");

        // Find the directional light in the scene for NDotL
        Light[] lights = FindObjectsOfType<Light>();
        foreach (Light light in lights)
        {
            if (light.type == LightType.Directional)
            {
                lightDirection = -light.transform.forward;
                lightColor = light.color * light.intensity;
                break;
            }
        }
    }

    void OnDestroy()
    {
        outputRT?.Release();
        rayBuffer?.Release();
        rayHitBuffer?.Release();
        cmd?.Release();
    }

    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        if (bvhScene == null || !bvhScene.CanRender())
        {
            Graphics.Blit(source, destination);
            return;
        }

        outputWidth   = sourceCamera.scaledPixelWidth;
        outputHeight  = sourceCamera.scaledPixelHeight;
        totalRays     = outputWidth * outputHeight;
        int dispatchX = Mathf.CeilToInt(totalRays / 64.0f);

        // Prepare buffers and output texture
        Utilities.PrepareBuffer(ref rayBuffer, totalRays, RayStructSize);
        Utilities.PrepareBuffer(ref rayHitBuffer, totalRays, RayHitStructSize);
        Utilities.PrepareRenderTexture(ref outputRT, outputWidth, outputHeight, RenderTextureFormat.ARGB32);

        // Generate Rays
        cmd.BeginSample("Ray Generation");
        {
            PrepareShader(cmd, rayGenerationShader, 0);

            cmd.SetComputeMatrixParam(rayGenerationShader, "CamInvProj", sourceCamera.projectionMatrix.inverse);
            cmd.SetComputeMatrixParam(rayGenerationShader, "CamToWorld", sourceCamera.cameraToWorldMatrix);

            cmd.DispatchCompute(rayGenerationShader, 0, dispatchX, 1, 1);

        }
        cmd.EndSample("Ray Generation");

        // Perform Intersection
        cmd.BeginSample("Ray Intersection");
        {
            PrepareShader(cmd, rayIntersectionShader, 0);
            bvhScene.PrepareShader(cmd, rayIntersectionShader, 0);

            cmd.DispatchCompute(rayIntersectionShader, 0, dispatchX, 1, 1);

        }
        cmd.EndSample("Ray Intersection");

        // Shade the results
        cmd.BeginSample("Ray Shading");
        {
            PrepareShader(cmd, rayShadingShader, 0);
            bvhScene.PrepareShader(cmd, rayShadingShader, 0);

            // Set shader keywords based on which display mode is enabled
            rayShadingShader.SetKeyword(outputNDotL,        display == DisplayMode.NDotL);
            rayShadingShader.SetKeyword(outputBVHSteps,     display == DisplayMode.BVHSteps);
            rayShadingShader.SetKeyword(outputBarycentrics, display == DisplayMode.Barycentrics);
            rayShadingShader.SetKeyword(outputNormals,      display == DisplayMode.Normals);
            rayShadingShader.SetKeyword(outputUVs,          display == DisplayMode.UV);

            cmd.DispatchCompute(rayShadingShader, 0, dispatchX, 1, 1);
        }
        cmd.EndSample("Ray Shading");

        // Overwrite image with output from raytracer
        cmd.Blit(outputRT, destination);

        Graphics.ExecuteCommandBuffer(cmd);
        cmd.Clear();
    }

    private void PrepareShader(CommandBuffer cmd, ComputeShader shader, int kernelIndex)
    {
        cmd.SetComputeVectorParam(shader, "LightDirection", lightDirection);
        cmd.SetComputeVectorParam(shader, "LightColor", lightColor);
        cmd.SetComputeFloatParam(shader, "FarPlane", sourceCamera.farClipPlane);
        cmd.SetComputeIntParam(shader, "OutputWidth", outputWidth);
        cmd.SetComputeIntParam(shader, "OutputHeight", outputHeight);
        cmd.SetComputeIntParam(shader, "TotalRays", totalRays);
        cmd.SetComputeBufferParam(shader, kernelIndex, "RayBuffer", rayBuffer);
        cmd.SetComputeBufferParam(shader, kernelIndex, "RayHitBuffer", rayHitBuffer);
        cmd.SetComputeTextureParam(shader, kernelIndex, "Output", outputRT);
    }

    // -- Debug Functions --

    [ContextMenu("Debug/Cast Ray CPU")]
    private void DebugCastRayCPU()
    {
        Vector3 pos = sourceCamera.transform.position;
        Vector3 dir = sourceCamera.transform.forward;

        tinybvh.BVH.Intersection intersection = bvhScene.Intersect(pos, dir, false);
        if (intersection.t < sourceCamera.farClipPlane)
        {
            BVHScene.BVHMesh mesh = bvhScene.GetMesh((int)intersection.inst);
            Debug.Log("Ray Hit: " + mesh.meshRenderer.name + "(" + intersection.inst + "). Distance: " + intersection.t + ", Triangle Index: " + intersection.prim);
            Debug.DrawLine(pos, pos + (dir * intersection.t), Color.red, 10.0f);
        } 
        else
        {
            Debug.Log("Ray did not hit anything.");
        }
    }

    [ContextMenu("Debug/Cast Ray GPU")]
    private void DebugCastRayGPU()
    {
        CommandBuffer debugCmd = new CommandBuffer();
        ComputeBuffer debugRayBuffer = new ComputeBuffer(1, RayStructSize, ComputeBufferType.Structured);
        ComputeBuffer debugRayHitBuffer = new ComputeBuffer(1, RayHitStructSize, ComputeBufferType.Structured);

        Vector3 pos = sourceCamera.transform.position;
        Vector3 dir = sourceCamera.transform.forward;

        // Create and upload ray
        List<Utilities.DebugRay> debugRays = new List<Utilities.DebugRay>();
        Utilities.DebugRay ray = new Utilities.DebugRay();
        ray.origin = pos;
        ray.direction = dir;
        debugRays.Add(ray);
        debugRayBuffer.SetData(debugRays);

        // Execute ray intersection shader
        bvhScene.PrepareShader(debugCmd, rayIntersectionShader, 0);
        debugCmd.SetComputeFloatParam(rayIntersectionShader, "FarPlane", sourceCamera.farClipPlane);
        debugCmd.SetComputeIntParam(rayIntersectionShader, "OutputWidth", 1);
        debugCmd.SetComputeIntParam(rayIntersectionShader, "OutputHeight", 1);
        debugCmd.SetComputeIntParam(rayIntersectionShader, "TotalRays", 1);
        debugCmd.SetComputeBufferParam(rayIntersectionShader, 0, "RayBuffer", debugRayBuffer);
        debugCmd.SetComputeBufferParam(rayIntersectionShader, 0, "RayHitBuffer", debugRayHitBuffer);
        debugCmd.DispatchCompute(rayIntersectionShader, 0, 1, 1, 1);

        Graphics.ExecuteCommandBuffer(debugCmd);
        debugCmd.Release();

        // Read back hit
        Utilities.DebugRayHit[] debugRayHits = new Utilities.DebugRayHit[1];
        debugRayHitBuffer.GetData(debugRayHits);
        Utilities.DebugRayHit hit = debugRayHits[0];

        if (hit.t < sourceCamera.farClipPlane)
        {
            BVHScene.BVHMesh mesh = bvhScene.GetMesh((int)hit.instIndex);
            Debug.Log("Ray Hit: " + mesh.meshRenderer.name + "(" + hit.instIndex + "). Distance: " + hit.t + ", Triangle Index: " + hit.triIndex);
            Debug.DrawLine(pos, pos + (dir * hit.t), Color.red, 10.0f);
        } 
        else
        {
            Debug.Log("Ray did not hit anything.");
        }

        debugRayBuffer.Release();
        debugRayHitBuffer.Release();
    }

    [ContextMenu("Debug/Trace Scene CPU")]
    private void DebugTraceSceneCPU()
    {
        outputWidth  = sourceCamera.scaledPixelWidth;
        outputHeight = sourceCamera.scaledPixelHeight;

        Texture2D texture = new Texture2D(outputWidth, outputHeight, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[outputWidth * outputHeight];

        Matrix4x4 CamInvProj = sourceCamera.projectionMatrix.inverse;
        Matrix4x4 CamToWorld = sourceCamera.cameraToWorldMatrix;

        for (int y = 0; y < outputHeight; y++)
        {
            for (int x = 0; x < outputWidth; x++)
            {
                int pixelIndex = (y * outputWidth) + x;
                pixels[pixelIndex] = Color.black;

                Vector3 origin = CamToWorld.MultiplyPoint(new Vector3(0.0f, 0.0f, 0.0f));

                // Compute world space direction
                Vector2 uv = new Vector2(((float)x / outputWidth) * 2.0f - 1.0f, ((float)y / outputHeight) * 2.0f - 1.0f);
                Vector4 clipSpacePos = new Vector4(uv.x, uv.y, -1.0f, 1.0f); // Near plane in NDC
                Vector4 viewSpacePos = CamInvProj * clipSpacePos;
                Vector3 direction = new Vector3(viewSpacePos.x, viewSpacePos.y, viewSpacePos.z).normalized;
                direction = CamToWorld.MultiplyVector(direction).normalized;

                tinybvh.BVH.Intersection intersection = bvhScene.Intersect(origin, direction, false);
                if (intersection.t < sourceCamera.farClipPlane)
                {
                    float dist = 1.0f - (intersection.t / 100.0f);
                    pixels[pixelIndex] = new Color(dist, dist, dist);
                }
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();

        // Save to PNG
        byte[] pngData = texture.EncodeToPNG();
        if (pngData != null)
        {
            System.IO.File.WriteAllBytes("CPUTrace.png", pngData);
            Debug.Log("CPU Trace written to CPUTrace.png");
        }
        else
        {
            Debug.LogError("Failed to encode texture to PNG.");
        }

        // Clean up the texture
        Destroy(texture);
    }
}
