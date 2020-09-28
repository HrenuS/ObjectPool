namespace ObjectPool.Pool.Models
{
    public class PoolStatistics
    {
        private PoolStatistics(in int numberObjects, in int idleCount, in int busyCount)
        {
            NumberObjects = numberObjects;
            IdleCount = idleCount;
            BusyCount = busyCount;
        }

        public int NumberObjects { get; }

        public int IdleCount { get; }

        public int BusyCount { get; }

        public static PoolStatistics Create(int numberObjects, int IdleCount, int busyCount)
            => new PoolStatistics(numberObjects, IdleCount, busyCount);
    }
}