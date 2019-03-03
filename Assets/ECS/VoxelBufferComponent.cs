using Unity.Entities;
using Unity.Rendering;

// Voxel buffer component
// Instantiates lots of voxels for instanced mesh renderers.

[System.Serializable]
struct VoxelBuffer : ISharedComponentData
{
    public int MaxVoxelCount;
    public RenderMesh RendererSettings;
}

class VoxelBufferComponent : SharedComponentDataProxy<VoxelBuffer> {}
