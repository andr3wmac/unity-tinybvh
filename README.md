# unity-tinybvh

An example implementation for [tinybvh](https://github.com/jbikker/tinybvh) in Unity and a foundation for building compute based raytracing solutions.

## Features

- **TinyBVH Integration**  
  Leverages [tinybvh](https://github.com/jbikker/tinybvh) for efficient BVH and Compact Wide BVH (CWBVH) construction, using a [C++ plugin](https://github.com/andr3wmac/unity-tinybvh/tree/main/Plugin).

- **GPU Mesh Aggregation**  
  Packs scene meshes into a unified aggregate vertex buffer and triangle attribute buffer via compute shader.

- **Asynchronous BVH Construction**  
  Uses async readback to transfer mesh data from GPU to CPU, and runs BVH construction in a thread.

- **TLAS Support**  
  Treats each MeshRenderer as a BLAS and rebuilds a TLAS every frame to support objects with changing transforms.

- **Wavefront Path Tracer**  
  A basic compute based raytracer structured like a wavefront path tracer. Includes multiple display mode examples:
  - **ndotl**
  - **BVH Steps**
  - **Ray Distance**
  - **Normals**
  - **Barycentrics**
  - **UVs**

![image](https://github.com/user-attachments/assets/1081fd85-895e-4cd6-ab07-0b46f9f0de62)

## Future Plans
- Further optimization
- SkinnedMeshRenderer support
