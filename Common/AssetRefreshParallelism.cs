using System;

namespace Common
{
    /// <summary>
    /// Provides a conservative default degree of parallelism for local asset refresh work.
    /// </summary>
    public static class AssetRefreshParallelism
    {
        private const int MinimumDegreeOfParallelism = 2;
        private const int MaximumDegreeOfParallelism = 6;

        /// <summary>
        /// Returns a bounded degree of parallelism sized for local disk-heavy batch work.
        /// </summary>
        public static int GetDegreeOfParallelism(int workItemCount)
        {
            if (workItemCount <= 1)
            {
                return 1;
            }

            int processorBound = Math.Max(MinimumDegreeOfParallelism, Environment.ProcessorCount / 2);
            return Math.Min(workItemCount, Math.Min(MaximumDegreeOfParallelism, processorBound));
        }
    }
}
