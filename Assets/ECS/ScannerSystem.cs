using Unity.Entities;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Transforms;
using Unity.Mathematics;
using UnityEngine;
using Unity.Burst;
// Scanner system
// Moves old voxels to points where rays hit colliders.

[UpdateAfter(typeof(VoxelAnimationSystem))]
unsafe class ScannerSystem : JobComponentSystem
{
    // Groups used for querying scanner/voxel components
    EntityQuery _scannerGroup;
    EntityQuery _voxelGroup;

    NativeArray<Translation> m_preTranslation;
    NativeArray<Scale> m_preScale;
    NativeArray<Translation> m_preOrigins;
    NativeArray<Scanner> m_preScanners;

    // Pointer used for sharing a counter between transfer jobs.
    // Why pointer? A: In burst, loading from/storing to static member fields
    // isn't allowed. So, we have to use a pointer to share a counter between
    // jobs in a static-ish fashion
    int* _pTransformCount;

    // Set-up job: Set up raycast commands in parallel.
    //[Unity.Burst.BurstCompile]
    struct SetupJob : IJobParallelFor
    {
        // Output
        public NativeArray<RaycastCommand> Commands;

        // Common parameters
        public float3 Origin;
        public float3 Extent;
        public int2 Resolution;

        public void Execute(int i)
        {
            var ix = i % Resolution.x;
            var iy = i / Resolution.x;

            var x = math.lerp(-Extent.x, Extent.x, (float)ix / Resolution.x);
            var y = math.lerp(-Extent.y, Extent.y, (float)iy / Resolution.y);

            var p = Origin + new float3(x, y, -Extent.z);
            Commands[i] = new RaycastCommand(p, new float3(0, 0, 1), Extent.z * 2);
        }
    }

    // Transfer job: Transfers raycast results to voxels in parallel.
    //[Unity.Burst.BurstCompile]
    struct TransferJob : IJobParallelFor
    {
        // Input arrays; Will be automatically deallocated.
        [DeallocateOnJobCompletion] [ReadOnly]
        public NativeArray<RaycastCommand> RaycastCommands;

        [DeallocateOnJobCompletion] [ReadOnly]
        public NativeArray<RaycastHit> RaycastHits;

        // Output array; Not govened by parallel-for
        [NativeDisableParallelForRestriction]
        public NativeArray<Translation> Positions;
        //We do need to initialize the start scale for each voxel
        [NativeDisableParallelForRestriction]
        public NativeArray<Scale> Scales;

        // Transform counter; Shared between jobs via the pointer
        [NativeDisableUnsafePtrRestriction] public int* pCounter;

        // Common parameters
        public float Scale;

        public void Execute(int index)
        {
            // Hit test
            if (RaycastHits[index].distance <= 0) return;

            // Retrieve the result.
            var from = (float3)RaycastCommands[index].from;
            var dist = RaycastHits[index].distance;

            var p = from + new float3(0, 0, dist);
       
            //var matrix = new float4x4(
            //    new float4(Scale, 0, 0, 0),
            //    new float4(0, Scale, 0, 0),
            //    new float4(0, 0, Scale, 0),
            //    new float4(p.x, p.y, p.z, 1));

            // Increment the output counter in a thread safe way.
            var count = System.Threading.Interlocked.Increment(ref *pCounter) - 1;

            // Output
            Positions[count % Positions.Length] = new Translation { Value = p };
            Scales[count % Scales.Length] = new Scale { Value = Scale };
        }
    }


    [BurstCompile]
    [RequireComponentTag(typeof(Voxel))]
    struct SetTranslationAndScale : IJobForEachWithEntity<Translation,Scale>
    {
        [ReadOnly] public NativeArray<Translation> targetTranslations;
        [ReadOnly] public NativeArray<Scale> targetScales;

        public void Execute(Entity entity, int index, ref Translation translation, ref Scale scale)
        {
            var targetPosition = targetTranslations[index];
            var targetScale = targetScales[index];
            translation = targetPosition;
            scale = targetScale;
        }
    }

    // Build a job chain with a given scanner.
    JobHandle BuildJobChain(float3 origin, Scanner scanner, JobHandle deps)
    {
        // Transform output destination
        //var transforms = _voxelGroup.GetComponentDataArray<TransformMatrix>();
        var positions = _voxelGroup.ToComponentDataArray<Translation>(Allocator.TempJob);
        var scales = _voxelGroup.ToComponentDataArray<Scale>(Allocator.TempJob);
        //Debug.Log("positions.Length:" + positions.Length + ",scales.Length:" + scales.Length);
        if (positions.Length == 0) return deps;

        if (_pTransformCount == null)
        {
            // Initialize the transform counter.
            _pTransformCount = (int*)UnsafeUtility.Malloc(
                sizeof(int), sizeof(int), Allocator.Persistent);
            *_pTransformCount = 0;
        }
        else
        {
            // Wrap around the transform counter to avoid overlfow.
            *_pTransformCount %= positions.Length;
        }

        // Total count of rays
        var total = scanner.Resolution.x * scanner.Resolution.y;

        // Ray cast command/result array
        var commands = new NativeArray<RaycastCommand>(total, Allocator.TempJob);
        var hits = new NativeArray<RaycastHit>(total, Allocator.TempJob);

        // 1: Set-up jobs
        var setupJob = new SetupJob {
            Commands = commands,
            Origin = origin,
            Extent = scanner.Extent,
            Resolution = scanner.Resolution
        };
        deps = setupJob.Schedule(total, 64, deps);

        // 2: Raycast jobs
        deps = RaycastCommand.ScheduleBatch(commands, hits, 16, deps);

        // 3: Transfer jobs
        var transferJob = new TransferJob {
            RaycastCommands = commands,
            RaycastHits = hits,
            Scale = 1f,//scanner.Extent.x * 2 / scanner.Resolution.x,
            Positions = positions,
            Scales = scales,
            pCounter = _pTransformCount
        };
        deps = transferJob.Schedule(total, 64, deps);

        // 4: SetTranslationAndScale jobs
        var setJob = new SetTranslationAndScale
        {
            targetTranslations = positions,
            targetScales = scales
        };
        deps = setJob.Schedule(_voxelGroup,deps);

        if (m_preTranslation.Length != 0)
        {
            m_preTranslation.Dispose();
            m_preScale.Dispose();
        }
        m_preTranslation = positions;
        m_preScale = scales;

        return deps;
    }

    protected override void OnCreateManager()
    {
        _scannerGroup = GetEntityQuery(typeof(Scanner), typeof(Translation));
        _voxelGroup = GetEntityQuery(typeof(Voxel), typeof(Translation), typeof(Scale));
    }

    protected override void OnDestroyManager()
    {
        if (_pTransformCount != null)
        {
            UnsafeUtility.Free(_pTransformCount, Allocator.Persistent);
            _pTransformCount = null;
        }
    }

    protected override JobHandle OnUpdate(JobHandle inputDeps)
    {
        // Build job chains for each scanner instance.
        var origins = _scannerGroup.ToComponentDataArray<Translation>(Allocator.TempJob);
        var scanners = _scannerGroup.ToComponentDataArray<Scanner>(Allocator.TempJob);
        inputDeps.Complete();
        for (var i = 0; i < scanners.Length; i++)
            inputDeps = BuildJobChain(origins[i].Value, scanners[i], inputDeps);

        if (m_preOrigins.Length != 0)
        {
            m_preOrigins.Dispose();
            m_preScanners.Dispose();
        }
        m_preOrigins = origins;
        m_preScanners = scanners;

        return inputDeps;
    }

    protected override void OnStopRunning()
    {
        if (m_preTranslation.Length != 0)
        {
            m_preTranslation.Dispose();
            m_preScale.Dispose();
            m_preOrigins.Dispose();
            m_preScanners.Dispose();
        }
    }
}
