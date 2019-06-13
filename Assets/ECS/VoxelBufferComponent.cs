using System;
using Unity.Entities;
using Unity.Rendering;
// Voxel buffer component
// Instantiates lots of voxels for instanced mesh renderers.

[System.Serializable]
struct VoxelBuffer : ISharedComponentData, IEquatable<VoxelBuffer>
{
    public int MaxVoxelCount;
    public RenderMesh RendererSettings;

    public bool Equals(VoxelBuffer other)
    {
        return
            MaxVoxelCount == other.MaxVoxelCount &&
            RendererSettings.Equals(other.RendererSettings);
    }

    public override int GetHashCode()
    {
        int hash = MaxVoxelCount.GetHashCode();

        if (!ReferenceEquals(RendererSettings, null))
            hash ^= RendererSettings.GetHashCode();

        return hash;
    }
}

class VoxelBufferComponent : SharedComponentDataProxy<VoxelBuffer> {}
