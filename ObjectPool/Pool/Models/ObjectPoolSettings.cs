using System;

namespace ObjectPool.Pool.Models
{
    public class ObjectPoolSettings
    {
        public int MaxPoolSize { get; set; }
        
        public int MinPoolSize { get; set; }
        
        public TimeSpan TimeOut { get; set; }
    }
}