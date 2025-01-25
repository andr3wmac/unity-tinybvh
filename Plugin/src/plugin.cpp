#include <deque>
#include <mutex>
#include <vector>

#define TINYBVH_IMPLEMENTATION
#include "tinybvh/tiny_bvh.h"
#include "plugin.h"

struct BVHContainer
{
    tinybvh::BVH4_CPU* bvh4CPU  = nullptr;
    tinybvh::BVH8_CWBVH* cwbvh  = nullptr;
};

// Global container for BVHs and a mutex for thread safety
std::deque<BVHContainer*> gBVHs;
std::mutex gBVHMutex;

// Adds a bvh to the global list either reusing an empty slot or making a new one.
int AddBVH(BVHContainer* newBVH)
{
    std::lock_guard<std::mutex> lock(gBVHMutex);

    // Look for a free entry to reuse
    for (size_t i = 0; i < gBVHs.size(); ++i) 
    {
        if (gBVHs[i] == nullptr) 
        {
            gBVHs[i] = newBVH;
            return static_cast<int>(i);
        }
    }

    // If no free entry is found, append a new one
    gBVHs.push_back(newBVH);
    return static_cast<int>(gBVHs.size() - 1);
}

// Fetch a pointer to a BVH by index, or nullptr if the index is invalid
BVHContainer* GetBVH(int index)
{
    std::lock_guard<std::mutex> lock(gBVHMutex);
    if (index >= 0 && index < static_cast<int>(gBVHs.size())) 
    {
        return gBVHs[index];
    }
    return nullptr;
}

int BuildBVH(tinybvh::bvhvec4* vertices, int triangleCount, bool buildCWBVH)
{
    BVHContainer* container = new BVHContainer();

    container->bvh4CPU = new tinybvh::BVH4_CPU();
    container->bvh4CPU->Build(vertices, triangleCount);

    if (buildCWBVH)
    {
        container->cwbvh = new tinybvh::BVH8_CWBVH();
        container->cwbvh->Build(vertices, triangleCount);
    }
    
    return AddBVH(container);
}

void DestroyBVH(int index) 
{
    std::lock_guard<std::mutex> lock(gBVHMutex);
    if (index >= 0 && index < static_cast<int>(gBVHs.size())) 
    {
        if (gBVHs[index] != nullptr)
        {
            if (gBVHs[index]->cwbvh != nullptr)
            {
                delete gBVHs[index]->cwbvh;
            }

            if (gBVHs[index]->bvh4CPU != nullptr)
            {
                delete gBVHs[index]->bvh4CPU;
            }

            delete gBVHs[index];
            gBVHs[index] = nullptr;
        }
    }
}

bool IsBVHReady(int index)
{
    BVHContainer* bvh = GetBVH(index);
    return (bvh != nullptr);
}

tinybvh::Intersection Intersect(int index, tinybvh::bvhvec3 origin, tinybvh::bvhvec3 direction, bool useCWBVH)
{
    BVHContainer* bvh = GetBVH(index);
    if (bvh != nullptr)
    {
        tinybvh::Ray ray(origin, direction);
        if (useCWBVH && bvh->cwbvh != nullptr)
        {
            bvh->cwbvh->Intersect(ray);
        }
        else 
        {
            bvh->bvh4CPU->Intersect(ray);
        }
        return ray.hit;
    }
    return tinybvh::Intersection();
}

int GetCWBVHNodesSize(int index)
{
    BVHContainer* bvh = GetBVH(index);
    return (bvh != nullptr && bvh->cwbvh != nullptr) ? bvh->cwbvh->usedBlocks * 16 : 0;
}

int GetCWBVHTrisSize(int index) 
{
    BVHContainer* bvh = GetBVH(index);
    return (bvh != nullptr && bvh->cwbvh != nullptr) ? bvh->cwbvh->triCount * 3 * 16 : 0;
}

bool GetCWBVHData(int index, tinybvh::bvhvec4** bvhNodes, tinybvh::bvhvec4** bvhTris) 
{
    BVHContainer* bvh = GetBVH(index);
    if (bvh == nullptr || bvh->cwbvh == nullptr)
    {
        return false;
    }

    if (bvh->cwbvh->bvh8Data != nullptr && bvh->cwbvh->bvh8Tris != nullptr)
    {
        *bvhNodes = bvh->cwbvh->bvh8Data;
        *bvhTris  = bvh->cwbvh->bvh8Tris;
        return true;
    }

    return false;
}