using System;
using System.Collections.Generic;
using UnityEditor.Profiling;
using UnityEditorInternal;

namespace ZWebGLFrameWork.Editor
{
    [Serializable]
    internal sealed class SimulationProfilerEvidence
    {
        public int entityCount;
        public string rawProfile;
        public int measuredAllocationEvents;
        public int profiledAllocationEvents;
        public long profiledBytes;
        public SimulationProfilerAllocation[] allocations;
    }

    [Serializable]
    internal sealed class SimulationProfilerAllocation
    {
        public string stage;
        public string path;
        public int eventsPerTick;
        public long bytesPerTick;
        public long[] sampleBytes;
        public string[] metadata;
        public string rawCallstack;
    }

    internal static class SimulationPerformanceProfilerEvidence
    {
        internal static SimulationProfilerEvidence Read(int entityCount,
            string rawPath, int measuredEvents)
        {
            SimulationProfilerAllocation[] allocations = ReadAllocations();
            int profiledEvents = 0;
            long profiledBytes = 0;
            for (int index = 0; index < allocations.Length; index++)
            {
                profiledEvents += allocations[index].eventsPerTick;
                profiledBytes += allocations[index].bytesPerTick;
            }
            return new SimulationProfilerEvidence
            {
                entityCount = entityCount,
                rawProfile = rawPath,
                measuredAllocationEvents = measuredEvents,
                profiledAllocationEvents = profiledEvents,
                profiledBytes = profiledBytes,
                allocations = allocations,
            };
        }

        private static SimulationProfilerAllocation[] ReadAllocations()
        {
            var byPath = new Dictionary<string, SimulationProfilerAllocation>(
                StringComparer.Ordinal);
            int first = ProfilerDriver.firstFrameIndex;
            int last = ProfilerDriver.lastFrameIndex;
            using (var iterator = new ProfilerFrameDataIterator())
            {
                for (int frame = first; frame >= 0 && frame <= last; frame++)
                {
                    int threadCount = iterator.GetThreadCount(frame);
                    for (int thread = 0; thread < threadCount; thread++)
                    using (HierarchyFrameDataView view =
                        ProfilerDriver.GetHierarchyFrameDataView(frame, thread,
                            HierarchyFrameDataView.ViewModes.Default,
                            HierarchyFrameDataView.columnDontSort, false))
                    {
                        if (view != null && view.valid)
                            Collect(view, view.GetRootItemID(), byPath);
                    }
                }
            }
            var allocations = new List<SimulationProfilerAllocation>(byPath.Values);
            allocations.Sort(CompareAllocations);
            return allocations.ToArray();
        }

        private static void Collect(HierarchyFrameDataView view, int item,
            Dictionary<string, SimulationProfilerAllocation> byPath)
        {
            string path = view.GetItemPath(item);
            if (view.GetItemName(item) == "GC.Alloc" &&
                path.Contains(SimulationPerformanceProfilerCapture.MarkerName))
            {
                int events = view.GetItemMergedSamplesCount(item);
                long[] sampleBytes = SampleBytes(view, item);
                long bytes = (long)Math.Round(view.GetItemColumnDataAsDouble(item,
                    HierarchyFrameDataView.columnGcMemory));
                if (!byPath.TryGetValue(path, out SimulationProfilerAllocation allocation))
                {
                    allocation = new SimulationProfilerAllocation
                    {
                        stage = Stage(path),
                        path = path,
                        sampleBytes = sampleBytes,
                        metadata = Metadata(view, item),
                        rawCallstack = view.ResolveItemMergedSampleCallstack(item, 0),
                    };
                    byPath.Add(path, allocation);
                }
                else
                {
                    allocation.sampleBytes = Append(allocation.sampleBytes, sampleBytes);
                }
                allocation.eventsPerTick += events;
                allocation.bytesPerTick += bytes;
            }
            var children = new List<int>();
            view.GetItemChildren(item, children);
            for (int index = 0; index < children.Count; index++)
                Collect(view, children[index], byPath);
        }

        private static string[] Metadata(HierarchyFrameDataView view, int item)
        {
            int count = view.GetItemMergedSamplesMetadataCount(item, 0);
            var values = new string[count];
            for (int index = 0; index < count; index++)
                values[index] = view.GetItemMergedSamplesMetadata(item, 0, index);
            return values;
        }

        private static long[] SampleBytes(HierarchyFrameDataView view, int item)
        {
            var values = new List<double>();
            view.GetItemMergedSamplesColumnDataAsDoubles(item,
                HierarchyFrameDataView.columnGcMemory, values);
            var bytes = new long[values.Count];
            for (int index = 0; index < values.Count; index++)
                bytes[index] = (long)Math.Round(values[index]);
            return bytes;
        }

        private static long[] Append(long[] left, long[] right)
        {
            var combined = new long[left.Length + right.Length];
            Array.Copy(left, 0, combined, 0, left.Length);
            Array.Copy(right, 0, combined, left.Length, right.Length);
            return combined;
        }

        private static string Stage(string path)
        {
            if (path.Contains("SimSystemGroup.UpdateAll")) return "Systems";
            if (path.Contains("BuildFullFrame")) return "BuildFullFrame";
            if (path.Contains("BuildDeltaFrame")) return "BuildDeltaFrame";
            if (path.Contains("TickResult")) return "TickResult";
            if (path.Contains("CommandBuffer.Drain")) return "CommandBuffer.Drain";
            if (path.Contains("HashAcceptedSequences")) return "HashAcceptedSequences";
            if (path.Contains("CommandBuffer.HashInto")) return "CommandBuffer.HashInto";
            if (path.Contains("ComputeChecksum")) return "ComputeChecksum";
            if (path.Contains("BuildTickEvents")) return "BuildTickEvents";
            if (path.Contains("RefreshChecksum")) return "RefreshChecksum";
            if (path.Contains("SimModeProfile.get_WaveTicks")) return "ModeProfile.WaveTicks";
            return "Tick.Direct";
        }

        private static int CompareAllocations(SimulationProfilerAllocation left,
            SimulationProfilerAllocation right)
        {
            int events = right.eventsPerTick.CompareTo(left.eventsPerTick);
            return events != 0 ? events : string.CompareOrdinal(left.path, right.path);
        }
    }
}
