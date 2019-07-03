using System.Collections.Generic;
using Unity.Entities;
using Unity.Transforms;
using Unity.Collections;
using Unity.Rendering;
using Unity.Mathematics;
// Voxel buffer system
// Instantiates lots of voxels for instanced mesh renderers.

class VoxelBufferSystem : ComponentSystem
{
    // Used for enumerate buffer components
    List<VoxelBuffer> _uniques = new List<VoxelBuffer>();
    EntityQuery _group;

    // Voxel archetype used for instantiation
    EntityArchetype _voxelArchetype;

    // Instance counter used for generating voxel IDs
    static uint _counter;

    //collect the VoxelBuffer component and create an entity archetype
    //TODO: OnCreateManager change to 
    protected override void OnCreateManager()
    {
        _group = GetEntityQuery(typeof(VoxelBuffer));

        _voxelArchetype = EntityManager.CreateArchetype(
            typeof(Voxel), typeof(Translation), typeof(Scale), typeof(RenderMesh)
        );
    }

    protected override void OnUpdate()
    {
        // Enumerate all the buffers.
        EntityManager.GetAllUniqueSharedComponentData<VoxelBuffer>(_uniques);
        for (var i = 0; i < _uniques.Count; i++)
        {
            _group.SetFilter(_uniques[i]);

            // Get a copy of the entity array.
            // Don't directly use the iterator -- we're going to remove
            // the buffer components, and it will invalidate the iterator.
            var iterator = _group.ToEntityArray(Allocator.TempJob);
            var entities = new NativeArray<Entity>(iterator.Length, Allocator.TempJob);
            iterator.CopyTo(entities);

            // Instantiate voxels along with the buffer entities.
            for (var j = 0; j < entities.Length; j++)
            {
                // Create the first voxel.
                var voxel = EntityManager.CreateEntity(_voxelArchetype);
                EntityManager.SetComponentData(voxel, new Voxel { ID = _counter++ });
                //EntityManager.SetComponentData(voxel, new Translation { Value = new float3(10f,0f,0f)});
                //EntityManager.SetComponentData(voxel, new Scale { Value = 1f });
                EntityManager.SetSharedComponentData(voxel, _uniques[i].RendererSettings);

                // Make clones from the first voxel.
                var cloneCount = _uniques[i].MaxVoxelCount - 1;
                //if (cloneCount > 0)
                //{
                //    var clones = new NativeArray<Entity>(cloneCount, Allocator.TempJob);
                //    EntityManager.Instantiate(voxel, clones);
                //    for (var k = 0; k < cloneCount; k++)
                //        EntityManager.SetComponentData(clones[k], new Voxel { ID = _counter++ });
                //    clones.Dispose();
                //}
               
                if (cloneCount > 0)
                {
                    var clones = new NativeArray<Entity>(cloneCount, Allocator.Temp);
                    for (int k = 0; k < cloneCount; ++k)
                    {
                        clones[k] = PostUpdateCommands.Instantiate(voxel);
                    }
                    for (int k = 0; k < cloneCount; k++)
                    {
                        PostUpdateCommands.SetComponent(clones[k], new Voxel { ID = _counter++ });
                    }
                }
                // Remove the buffer component from the entity.
                EntityManager.RemoveComponent(entities[j], typeof(VoxelBuffer));
            }
            iterator.Dispose();
            entities.Dispose();
        }

        _uniques.Clear();
    }
}
